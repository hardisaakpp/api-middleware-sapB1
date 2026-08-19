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
    string? PreferredVendorCode,
    decimal? Pvp,
    string? CodeBars,
    string? Marca
);

public sealed record ItemStockResponse(
    string ItemCode,
    string WarehouseCode,
    decimal InStock,
    decimal IsCommitted,
    string? UomCode,
    bool Locked
);
