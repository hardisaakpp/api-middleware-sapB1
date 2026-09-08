using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Validations;

namespace Middleware.Application.UseCases.PurchaseOrders;

// Resolves DocNum -> DocEntry (Service Layer only indexes PurchaseOrders by DocEntry) and then
// enriches each line's CodeBars via GetItemsByCodesAsync — SAP does not return it on the
// PurchaseOrders document itself. This enrichment intentionally lives only here: the existing
// GET /purchase-orders/{docEntry} endpoint (GetPurchaseOrderUseCase) must not pay this extra
// SAP round trip for consumers that don't need it.
public sealed class GetPurchaseOrderByDocNumUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ValidationSettings _settings;
    private readonly ILogger<GetPurchaseOrderByDocNumUseCase> _logger;

    public GetPurchaseOrderByDocNumUseCase(
        ISapGateway gateway,
        IOptions<ValidationSettings> settings,
        ILogger<GetPurchaseOrderByDocNumUseCase> logger)
    {
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PurchaseOrderResponse?> ExecuteAsync(GetPurchaseOrderByDocNumRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resolving PurchaseOrder DocNum={DocNum} to DocEntry", request.DocNum);

        var docEntry = await _gateway.GetPurchaseOrderDocEntryByDocNumAsync(request.DocNum, cancellationToken);
        if (docEntry is null)
        {
            _logger.LogWarning("SAP PurchaseOrder DocNum={DocNum} not found", request.DocNum); // NOSONAR
            return null;
        }

        var order = await _gateway.GetPurchaseOrderAsync(docEntry.Value, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var itemCodes = order.Lines
            .Select(l => l.ItemCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (itemCodes.Count == 0)
        {
            return order;
        }

        var chunks = itemCodes.Chunk(Math.Max(1, _settings.SapChunkSize)).ToList();

        var codeBarsByItemCode = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var sync = new object();

        using var gate = new SemaphoreSlim(Math.Max(1, _settings.MaxParallelChunks));
        var chunkTasks = chunks.Select(async chunk =>
        {
            await gate.WaitAsync(CancellationToken.None);
            try
            {
                var items = await _gateway.GetItemsByCodesAsync(chunk, cancellationToken);
                lock (sync)
                {
                    foreach (var item in items)
                        codeBarsByItemCode[item.ItemCode] = item.CodeBars;
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        await Task.WhenAll(chunkTasks);

        var enrichedLines = order.Lines
            .Select(l => l with { CodeBars = codeBarsByItemCode.GetValueOrDefault(l.ItemCode) })
            .ToList();

        return order with { Lines = enrichedLines };
    }
}
