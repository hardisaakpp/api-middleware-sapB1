namespace Middleware.Application.Dtos.Responses;

public sealed record ItemResponse(
    string ItemCode,
    string ItemName,
    string? InventoryUoM,
    decimal OnHand,
    bool PurchaseItem,
    bool SalesItem,
    bool InventoryItem,
    bool Valid,
    bool Frozen,
    string? ItemType,
    string? PackCode,
    int? GroupCode,
    string? GroupName,
    string? PreferredVendorCode
);

public sealed record ItemStockResponse(
    string ItemCode,
    string WarehouseCode,
    decimal InStock,
    decimal IsCommitted,
    string? UomCode,
    bool Locked
);

public sealed record ItemsStockBatchResponse(
    IReadOnlyList<ItemStockResponse> Items,
    IReadOnlyList<string> NotFound
);
public sealed record ItemAvailableStockResponse(
    string ItemCode,
    string WarehouseCode,
    decimal Available
);

public sealed record ItemsAvailableStockBatchResponse(
    IReadOnlyList<ItemAvailableStockResponse> Items
);

