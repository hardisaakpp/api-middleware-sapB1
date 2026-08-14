using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Items;

/// <summary>
/// Combines the three existence checks api-articulo-maestro's has_historical_movements used to run
/// itself against SAP (PurchaseOrders, InventoryGenEntries, InventoryTransferRequests) into a single
/// call, so the caller gets one round trip instead of orchestrating the fan-out on its own.
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

        var purchaseOrdersTask = _gateway.HasPurchaseOrderLineForItemAsync(itemCode, cancellationToken);
        var inventoryGenEntriesTask = _gateway.HasInventoryGenEntryLineForItemAsync(itemCode, cancellationToken);
        var inventoryTransferRequestsTask = _gateway.HasInventoryTransferRequestLineForItemAsync(itemCode, cancellationToken);

        await Task.WhenAll(purchaseOrdersTask, inventoryGenEntriesTask, inventoryTransferRequestsTask);

        var hasPurchaseOrders = purchaseOrdersTask.Result;
        var hasInventoryGenEntries = inventoryGenEntriesTask.Result;
        var hasInventoryTransferRequests = inventoryTransferRequestsTask.Result;

        return new HasItemMovementsResponse(
            HasMovements: hasPurchaseOrders || hasInventoryGenEntries || hasInventoryTransferRequests,
            hasPurchaseOrders,
            hasInventoryGenEntries,
            hasInventoryTransferRequests);
    }
}
