namespace Middleware.Application.Dtos.Requests;

public sealed record NotifyWebPosStockTransferRequest(
    int DocEntry,
    int DocNum,
    string FromWarehouse,
    string ToWarehouse,
    IReadOnlyList<NotifyWebPosStockTransferLine> Lines
);

public sealed record NotifyWebPosStockTransferLine(string ItemCode, decimal Quantity);
