namespace Middleware.Application.Dtos.Responses;

// Recovered = true: el documento ya existía en SAP con esa clave de idempotencia y se devolvió el
// existente en vez de crear otro (mismo 201 para el módulo; el detalle queda visible en el job y api_log).
public sealed record StockTransferResponse(int DocEntry, int DocNum, WebPosSyncInfo? WebPosSync = null, bool Recovered = false);

public sealed record StockTransferStatusResponse(
    int DocEntry,
    int DocNum,
    string FromWarehouse,
    string ToWarehouse,
    IReadOnlyList<StockTransferLineStatusResponse> Lines
);

public sealed record StockTransferLineStatusResponse(
    string ItemCode,
    string ItemDescription,
    //quantity
    decimal SentQuantity,
    decimal RemainingOpenQuantity
);

