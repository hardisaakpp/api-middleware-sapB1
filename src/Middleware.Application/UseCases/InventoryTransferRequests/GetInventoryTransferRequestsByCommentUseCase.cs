using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.InventoryTransferRequests;

public sealed class GetInventoryTransferRequestsByCommentUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetInventoryTransferRequestsByCommentUseCase> _logger;

    public GetInventoryTransferRequestsByCommentUseCase(ISapGateway gateway, ILogger<GetInventoryTransferRequestsByCommentUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<InventoryTransferRequestsListResponse> ExecuteAsync(string commentsContain, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting InventoryTransferRequests with Comments containing '{CommentsContain}'", commentsContain);
        return await _gateway.GetInventoryTransferRequestsByCommentAsync(commentsContain, cancellationToken);
    }
}
