using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.Abstractions;

public interface IWebPosStockTransferNotifier
{
    Task<WebPosSyncInfo> NotifyAsync(
        int docEntry,
        int docNum,
        string fromWarehouse,
        string toWarehouse,
        IReadOnlyList<NotifyWebPosStockTransferLine> lines,
        CancellationToken cancellationToken);
}
