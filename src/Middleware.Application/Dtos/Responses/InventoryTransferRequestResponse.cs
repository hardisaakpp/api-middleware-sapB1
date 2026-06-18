namespace Middleware.Application.Dtos.Responses;

public sealed record InventoryTransferRequestsListResponse(
    IReadOnlyList<InventoryTransferRequestResponse> Value
);

public sealed record InventoryTransferRequestResponse(
    int DocEntry,
    int DocNum,
    string? Comments,
    string FromWarehouse,
    string ToWarehouse,
    string DocDate,
    string DocStatus,
    IReadOnlyList<InventoryTransferRequestLineResponse> Lines
);

public sealed record InventoryTransferRequestLineResponse(
    int LineNum,
    string ItemCode,
    decimal Quantity,
    string? WarehouseCode
);
