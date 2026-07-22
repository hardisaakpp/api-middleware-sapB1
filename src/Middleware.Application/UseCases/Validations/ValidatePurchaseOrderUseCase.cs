using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Validations;

public sealed class ValidatePurchaseOrderUseCase : IUseCase<ValidatePurchaseOrderRequest, ValidatePurchaseOrderResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ValidationSettings _settings;
    private readonly ILogger<ValidatePurchaseOrderUseCase> _logger;

    public ValidatePurchaseOrderUseCase(
        ISapGateway gateway,
        IOptions<ValidationSettings> settings,
        ILogger<ValidatePurchaseOrderUseCase> logger)
    {
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ValidatePurchaseOrderResponse> ExecuteAsync(
        ValidatePurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        // One result per unique (itemCode, warehouseCode) pair, preserving first-seen order
        var pairs = request.Lines
            .Select(l => (ItemCode: l.ItemCode.Trim(), WarehouseCode: l.WarehouseCode.Trim()))
            .Distinct()
            .ToList();

        var itemCodes = pairs.Select(p => p.ItemCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var chunks = itemCodes.Chunk(Math.Max(1, _settings.SapChunkSize)).ToList();

        _logger.LogInformation(
            "Validating purchase order batch: CardCode={CardCode}, Pairs={PairCount}, UniqueItems={ItemCount}, Chunks={ChunkCount}",
            request.CardCode ?? "(none)", pairs.Count, itemCodes.Count, chunks.Count);

        // Overall batch deadline: elements not resolved in time come back as connection_error
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.BatchTimeoutSeconds));

        var bpTask = string.IsNullOrWhiteSpace(request.CardCode)
            ? Task.FromResult<BusinessPartnerValidationResult?>(null)
            : ValidateBusinessPartnerAsync(request.CardCode!, cts.Token);

        var itemData = new Dictionary<string, SapItemValidationData>(StringComparer.OrdinalIgnoreCase);
        var itemErrors = new Dictionary<string, (string Type, string? Message)>(StringComparer.OrdinalIgnoreCase);
        var sync = new object();

        using var gate = new SemaphoreSlim(Math.Max(1, _settings.MaxParallelChunks));
        var chunkTasks = chunks.Select(async chunk =>
        {
            await gate.WaitAsync(CancellationToken.None);
            try
            {
                var data = await _gateway.GetItemsForValidationAsync(chunk, cts.Token);
                lock (sync)
                {
                    foreach (var d in data)
                    {
                        itemData[d.ItemCode] = d;
                    }
                }
            }
            catch (Exception ex)
            {
                // Partial failure: only this chunk's items are marked, the batch continues
                var error = ClassifyError(ex);
                _logger.LogWarning(ex,
                    "Validation chunk of {Count} items failed with {ErrorType}", chunk.Length, error.Type);
                lock (sync)
                {
                    foreach (var code in chunk)
                    {
                        itemErrors[code] = error;
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        await Task.WhenAll(chunkTasks);
        var businessPartner = await bpTask;

        var items = pairs
            .Select(p => MapItem(p.ItemCode, p.WarehouseCode, itemData, itemErrors))
            .ToList();

        return new ValidatePurchaseOrderResponse(businessPartner, items);
    }

    private async Task<BusinessPartnerValidationResult?> ValidateBusinessPartnerAsync(
        string cardCode, CancellationToken ct)
    {
        try
        {
            var bp = await _gateway.GetBusinessPartnerAsync(cardCode.Trim(), ct);
            if (bp is null)
            {
                return new BusinessPartnerValidationResult(
                    cardCode, Exists: false, CardName: null, CardType: null,
                    Valid: false, Frozen: false, ErrorType: ValidationErrorTypes.NotFound);
            }

            return new BusinessPartnerValidationResult(
                bp.CardCode, Exists: true, bp.CardName, bp.CardType,
                bp.Valid, bp.Frozen, ErrorType: null);
        }
        catch (Exception ex)
        {
            var (type, message) = ClassifyError(ex);
            _logger.LogWarning(ex, "BusinessPartner validation for {CardCode} failed with {ErrorType}", cardCode, type);
            return new BusinessPartnerValidationResult(
                cardCode, Exists: false, CardName: null, CardType: null,
                Valid: false, Frozen: false, ErrorType: type, ErrorMessage: message);
        }
    }

    private static ItemValidationResult MapItem(
        string itemCode,
        string warehouseCode,
        IReadOnlyDictionary<string, SapItemValidationData> itemData,
        IReadOnlyDictionary<string, (string Type, string? Message)> itemErrors)
    {
        if (itemErrors.TryGetValue(itemCode, out var error))
        {
            return new ItemValidationResult(
                itemCode, warehouseCode, Exists: false, ItemName: null,
                PurchaseItem: false, Valid: false, Frozen: false,
                WarehouseConfigured: false, InStock: null, IsCommitted: null,
                UomCode: null, Locked: false,
                ErrorType: error.Type, ErrorMessage: error.Message);
        }

        if (!itemData.TryGetValue(itemCode, out var item))
        {
            return new ItemValidationResult(
                itemCode, warehouseCode, Exists: false, ItemName: null,
                PurchaseItem: false, Valid: false, Frozen: false,
                WarehouseConfigured: false, InStock: null, IsCommitted: null,
                UomCode: null, Locked: false,
                ErrorType: ValidationErrorTypes.ItemNotFound);
        }

        var warehouse = item.Warehouses.FirstOrDefault(
            w => string.Equals(w.WarehouseCode, warehouseCode, StringComparison.OrdinalIgnoreCase));

        if (warehouse is null)
        {
            return new ItemValidationResult(
                item.ItemCode, warehouseCode, Exists: true, item.ItemName,
                item.PurchaseItem, item.Valid, item.Frozen,
                WarehouseConfigured: false, InStock: 0m, IsCommitted: 0m,
                item.InventoryUoM, Locked: false,
                ErrorType: ValidationErrorTypes.WarehouseNotFound);
        }

        return new ItemValidationResult(
            item.ItemCode, warehouse.WarehouseCode, Exists: true, item.ItemName,
            item.PurchaseItem, item.Valid, item.Frozen,
            WarehouseConfigured: true, warehouse.InStock, warehouse.Committed,
            item.InventoryUoM, warehouse.Locked,
            ErrorType: null);
    }

    private static (string Type, string? Message) ClassifyError(Exception ex) =>
        ValidationErrorClassifier.Classify(ex);
}

public sealed class ValidationSettings
{
    public const string SectionName = "Validation";

    /// <summary>Overall deadline for the whole batch, in seconds.</summary>
    public int BatchTimeoutSeconds { get; init; } = 60;

    /// <summary>Item codes per Service Layer $filter query. Keep ≤ 20 (SL page size).</summary>
    public int SapChunkSize { get; init; } = 20;

    /// <summary>Chunk queries in flight at once (SapThrottle still applies globally).</summary>
    public int MaxParallelChunks { get; init; } = 4;
}
