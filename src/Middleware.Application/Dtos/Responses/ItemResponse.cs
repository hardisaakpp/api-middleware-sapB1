namespace Middleware.Application.Dtos.Responses;

public sealed record ItemResponse(
    string ItemCode,
    string ItemName,
    string? InventoryUoM,
    decimal OnHand,
    bool PurchaseItem
);

public sealed record ItemStockResponse(
    string ItemCode,
    string WarehouseCode,
    decimal InStock
);
