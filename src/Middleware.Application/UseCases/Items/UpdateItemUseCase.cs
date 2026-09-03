using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Items;

public sealed class UpdateItemUseCase : IUseCase<UpdateItemRequest, UpdateItemResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<UpdateItemUseCase> _logger;

    public UpdateItemUseCase(ISapGateway gateway, ILogger<UpdateItemUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<UpdateItemResponse> ExecuteAsync(UpdateItemRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating Item ItemCode={ItemCode}", request.ItemCode); // NOSONAR

        await _gateway.UpdateItemAsync(request.ItemCode, request, cancellationToken);

        _logger.LogInformation("Item updated successfully: ItemCode={ItemCode}", request.ItemCode); // NOSONAR

        return new UpdateItemResponse(true, request.ItemCode);
    }
}
