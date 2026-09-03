using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.PriceLists;

public sealed class GetPriceListsUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetPriceListsUseCase> _logger;

    public GetPriceListsUseCase(ISapGateway gateway, ILogger<GetPriceListsUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PriceListsResponse> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting full price list catalog");
        var priceLists = await _gateway.GetPriceListsAsync(cancellationToken);
        return new PriceListsResponse(priceLists);
    }
}
