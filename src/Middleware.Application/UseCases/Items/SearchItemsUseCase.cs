using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Items;

public sealed class SearchItemsUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<SearchItemsUseCase> _logger;

    public SearchItemsUseCase(ISapGateway gateway, ILogger<SearchItemsUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ItemSearchResult>> ExecuteAsync(string query, int top, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Searching Items ItemCode/BarCode contains '{Query}' Top={Top}", query, top);
        return await _gateway.SearchItemsAsync(query, top, cancellationToken);
    }
}
