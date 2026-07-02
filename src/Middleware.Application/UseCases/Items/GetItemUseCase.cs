using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Items;

public sealed class GetItemUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetItemUseCase> _logger;

    public GetItemUseCase(ISapGateway gateway, ILogger<GetItemUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ItemResponse?> ExecuteAsync(string itemCode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting Item ItemCode={ItemCode}", itemCode);
        return await _gateway.GetItemAsync(itemCode, cancellationToken);
    }
}
