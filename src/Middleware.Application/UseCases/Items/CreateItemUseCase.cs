using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Items;

public sealed class CreateItemUseCase : IUseCase<CreateItemRequest, CreateItemResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreateItemUseCase> _logger;

    public CreateItemUseCase(ISapGateway gateway, ILogger<CreateItemUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<CreateItemResponse> ExecuteAsync(CreateItemRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating Item ItemCode={ItemCode}", request.ItemCode); // NOSONAR

        await _gateway.CreateItemAsync(request, cancellationToken);

        _logger.LogInformation("Item created: ItemCode={ItemCode}", request.ItemCode); // NOSONAR

        return new CreateItemResponse(request.ItemCode);
    }
}
