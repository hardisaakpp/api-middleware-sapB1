namespace Middleware.Application.Dtos.Responses;

public sealed record PurchaseOrderResponse(
    int DocEntry,
    int DocNum,
    string VendorCode,
    string DocDate,
    string DocStatus,
    IReadOnlyList<PurchaseOrderLineResponse> Lines
);

public sealed record PurchaseOrderLineResponse(
    int LineNum,
    string ItemCode,
    decimal Quantity,
    decimal OpenQuantity,
    decimal UnitPrice,
    string WarehouseCode
);

public sealed record UpdatePurchaseOrderResponse(bool Success, int DocEntry);

public sealed record PurchaseOrdersListResponse(
    IReadOnlyList<System.Text.Json.Nodes.JsonObject> Value,
    int Count
);
