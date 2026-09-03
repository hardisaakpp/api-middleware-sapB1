namespace Middleware.Application.Dtos.Responses;

public sealed record StockTransferResponse(int DocEntry, int DocNum, WebPosSyncInfo? WebPosSync = null);

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

