namespace Middleware.Application.Dtos.Responses;

public sealed record InventoryTransferRequestsListResponse(
    IReadOnlyList<InventoryTransferRequestResponse> Value
);

/// <summary>
/// Listado paginado crudo (JSON de SAP sin filtrar campos) — mismo patrón que
/// PurchaseOrdersListResponse: el caller (CEDI) parsea los campos que necesita con su
/// propio mapper tolerante, en vez de que el middleware preseleccione/renombre campos.
/// </summary>
public sealed record InventoryTransferRequestsRawListResponse(
    IReadOnlyList<System.Text.Json.Nodes.JsonObject> Value,
    int Count
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
    string? WarehouseCode,
    // Nuevos (GET individual, P-008 de api-cedi): confirmado en vivo contra TEST_MABEL que
    // el campo real en SAP Service Layer para InventoryTransferRequests es
    // "RemainingOpenQuantity" (igual que en PurchaseOrders) — NO el "OpenQuantity" que
    // hizo fallar ambos casos de PurchaseOrders. Quedan con default para no romper el
    // constructor posicional que ya usa GetInventoryTransferRequestsByCommentAsync.
    decimal RemainingOpenQuantity = 0m,
    string? ItemDescription = null,
    string? FromWarehouseCode = null
);

public sealed record UpdateInventoryTransferRequestStatusResponse(bool Success, int DocEntry, string U_STATUS);
