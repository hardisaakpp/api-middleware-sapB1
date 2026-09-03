using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Items;

/// <summary>
/// Uses the atomic item-master balance check exposed by SAP. Failures propagate so callers never
/// mistake an unavailable validation for an item without movements.
/// </summary>
public sealed class HasItemMovementsUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<HasItemMovementsUseCase> _logger;

    public HasItemMovementsUseCase(ISapGateway gateway, ILogger<HasItemMovementsUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<HasItemMovementsResponse> ExecuteAsync(string itemCode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking historical movements for ItemCode={ItemCode}", itemCode); // NOSONAR

        var hasMovements = await _gateway.HasItemMovementsAsync(itemCode, cancellationToken);
        return new HasItemMovementsResponse(itemCode, hasMovements);
    }
}
