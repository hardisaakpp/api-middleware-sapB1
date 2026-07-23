using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Validations;

namespace Middleware.Application.UseCases.Items;

public sealed class GetItemsBatchUseCase : IUseCase<GetItemsBatchRequest, ItemsBatchResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ValidationSettings _settings;
    private readonly ILogger<GetItemsBatchUseCase> _logger;

    public GetItemsBatchUseCase(
        ISapGateway gateway,
        IOptions<ValidationSettings> settings,
        ILogger<GetItemsBatchUseCase> logger)
    {
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ItemsBatchResponse> ExecuteAsync(
        GetItemsBatchRequest request, CancellationToken cancellationToken = default)
    {
        var codes = request.ItemCodes
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
        {
            return new ItemsBatchResponse([], []);
        }

        var chunks = codes.Chunk(Math.Max(1, _settings.SapChunkSize)).ToList();

        _logger.LogInformation(
            "Items batch: UniqueCodes={CodeCount}, Chunks={ChunkCount}", codes.Count, chunks.Count);

        // Unlike the validation batches, a connectivity failure here fails the WHOLE request
        // (no silent partial results): Task.WhenAll propagates the chunk's exception and the
        // global exception middleware turns it into a 5xx problem-detail.
        var found = new Dictionary<string, ItemResponse>(StringComparer.OrdinalIgnoreCase);
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
                    {
                        found[item.ItemCode] = item;
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        await Task.WhenAll(chunkTasks);

        var items = new List<ItemResponse>();
        var notFound = new List<string>();
        foreach (var code in codes)
        {
            if (found.TryGetValue(code, out var item))
            {
                items.Add(item);
            }
            else
            {
                notFound.Add(code);
            }
        }

        return new ItemsBatchResponse(items, notFound);
    }
}
