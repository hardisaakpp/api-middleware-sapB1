using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Items;

public sealed class DeleteItemUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<DeleteItemUseCase> _logger;

    public DeleteItemUseCase(ISapGateway gateway, ILogger<DeleteItemUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<DeleteItemResponse> ExecuteAsync(string itemCode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting Item ItemCode={ItemCode}", itemCode); // NOSONAR

        await _gateway.DeleteItemAsync(itemCode, cancellationToken);

        _logger.LogInformation("Item deleted: ItemCode={ItemCode}", itemCode); // NOSONAR

        return new DeleteItemResponse(itemCode);
    }
}
