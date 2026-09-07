using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Validations;

namespace Middleware.Application.UseCases.Warehouses;

public sealed class ValidateWarehousesUseCase : IUseCase<ValidateWarehousesRequest, ValidateWarehousesResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ValidationSettings _settings;
    private readonly ILogger<ValidateWarehousesUseCase> _logger;

    public ValidateWarehousesUseCase(
        ISapGateway gateway,
        IOptions<ValidationSettings> settings,
        ILogger<ValidateWarehousesUseCase> logger)
    {
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ValidateWarehousesResponse> ExecuteAsync(
        ValidateWarehousesRequest request, CancellationToken cancellationToken = default)
    {
        // One result per unique code, preserving first-seen order
        var codes = request.WarehouseCodes
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chunks = codes.Chunk(Math.Max(1, _settings.SapChunkSize)).ToList();

        _logger.LogInformation(
            "Validating warehouse batch: UniqueCodes={CodeCount}, Chunks={ChunkCount}", codes.Count, chunks.Count);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.BatchTimeoutSeconds));

        var found = new Dictionary<string, WarehouseResponse>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, (string Type, string? Message)>(StringComparer.OrdinalIgnoreCase);
        var sync = new object();

        using var gate = new SemaphoreSlim(Math.Max(1, _settings.MaxParallelChunks));
        var chunkTasks = chunks.Select(async chunk =>
        {
            await gate.WaitAsync(CancellationToken.None);
            try
            {
                var data = await _gateway.GetWarehousesByCodesAsync(chunk, cts.Token);
                lock (sync)
                {
                    foreach (var w in data)
                    {
                        found[w.WhsCode] = w;
                    }
                }
            }
            catch (Exception ex)
            {
                // Partial failure: only this chunk's codes are marked, the batch continues
                var error = ValidationErrorClassifier.Classify(ex);
                _logger.LogWarning(ex,
                    "Warehouse validation chunk of {Count} codes failed with {ErrorType}", chunk.Length, error.Type);
                lock (sync)
                {
                    foreach (var code in chunk)
                    {
                        errors[code] = error;
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        await Task.WhenAll(chunkTasks);

        var results = codes.Select(code =>
        {
            if (errors.TryGetValue(code, out var error))
            {
                return new WarehouseValidationResult(code, Exists: false, WhsName: null, error.Type, error.Message);
            }

            if (found.TryGetValue(code, out var warehouse))
            {
                return new WarehouseValidationResult(warehouse.WhsCode, Exists: true, warehouse.WhsName, ErrorType: null);
            }

            return new WarehouseValidationResult(code, Exists: false, WhsName: null, ValidationErrorTypes.NotFound);
        }).ToList();

        return new ValidateWarehousesResponse(results);
    }
}
