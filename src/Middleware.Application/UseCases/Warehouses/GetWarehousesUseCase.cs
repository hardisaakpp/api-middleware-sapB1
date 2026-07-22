using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Warehouses;

public sealed class GetWarehousesUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetWarehousesUseCase> _logger;

    public GetWarehousesUseCase(ISapGateway gateway, ILogger<GetWarehousesUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<WarehousesListResponse> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting full warehouse catalog");
        var warehouses = await _gateway.GetWarehousesAsync(cancellationToken);
        return new WarehousesListResponse(warehouses);
    }
}
