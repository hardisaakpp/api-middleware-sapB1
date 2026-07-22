namespace Middleware.Application.Dtos.Responses;

public sealed record ValidatePurchaseOrderResponse(
    BusinessPartnerValidationResult? BusinessPartner,
    IReadOnlyList<ItemValidationResult> Items
);

public sealed record BusinessPartnerValidationResult(
    string CardCode,
    bool Exists,
    string? CardName,
    string? CardType,
    bool Valid,
    bool Frozen,
    string? ErrorType,
    string? ErrorMessage = null
);

public sealed record ItemValidationResult(
    string ItemCode,
    string WarehouseCode,
    bool Exists,
    string? ItemName,
    bool PurchaseItem,
    bool Valid,
    bool Frozen,
    bool WarehouseConfigured,
    decimal? InStock,
    decimal? IsCommitted,
    string? UomCode,
    bool Locked,
    string? ErrorType,
    string? ErrorMessage = null
);

public static class ValidationErrorTypes
{
    public const string ItemNotFound = "item_not_found";
    public const string WarehouseNotFound = "warehouse_not_found";
    public const string NotFound = "not_found";
    public const string ConnectionError = "connection_error";
    public const string Error = "error";
}
