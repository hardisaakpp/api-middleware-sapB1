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

/// <summary>Resultado de búsqueda de ítems por ItemCode o BarCode (contains, no exacto) — para P-012 de api-cedi ("Consulta Stock").</summary>
public sealed record ItemSearchResult(
    string ItemCode,
    string ItemName,
    string? BarCode,
    string? InventoryUoM
);
