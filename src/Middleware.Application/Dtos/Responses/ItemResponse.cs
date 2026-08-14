using Middleware.Application.Abstractions;

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
    // Only populated by GetItemsByCodesAsync (POST /items/batch), which requests
    // ItemWarehouseInfoCollection in its $select; GetItemAsync (GET /items/{itemCode})
    // does not request it, so this stays null there. Additive field — does not change
    // the shape other callers of ItemResponse already rely on.
    IReadOnlyList<SapItemWarehouseData>? Warehouses = null
);

public sealed record ItemStockResponse(
    string ItemCode,
    string WarehouseCode,
    decimal InStock,
    decimal IsCommitted,
    string? UomCode,
    bool Locked
);
