using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.Abstractions;

public interface IWebPosGoodsReceiptNotifier
{
    Task<WebPosSyncInfo> NotifyAsync(
        int docEntry,
        int docNum,
        IReadOnlyList<NotifyWebPosGoodsReceiptLine> lines,
        CancellationToken cancellationToken);
}
