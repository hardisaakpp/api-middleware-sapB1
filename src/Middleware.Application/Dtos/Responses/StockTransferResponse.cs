namespace Middleware.Application.Dtos.Responses;

public sealed record StockTransferResponse(int DocEntry, int DocNum, WebPosSyncInfo? WebPosSync = null);
