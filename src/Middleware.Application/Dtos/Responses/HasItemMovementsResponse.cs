namespace Middleware.Application.Dtos.Responses;

public sealed record HasItemMovementsResponse(
    bool HasMovements,
    bool HasPurchaseOrders,
    bool HasInventoryGenEntries,
    bool HasInventoryTransferRequests
);
