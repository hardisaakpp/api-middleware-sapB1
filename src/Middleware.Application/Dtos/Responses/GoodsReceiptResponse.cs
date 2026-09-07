namespace Middleware.Application.Dtos.Responses;

public sealed record GoodsReceiptResponse(int DocEntry, int DocNum, WebPosSyncInfo? WebPosSync = null);
