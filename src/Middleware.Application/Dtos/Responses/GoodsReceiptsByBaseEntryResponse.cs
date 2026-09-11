using Middleware.Application.Abstractions;

namespace Middleware.Application.Dtos.Responses;

public sealed record GoodsReceiptsByBaseEntryResponse(IReadOnlyList<GoodsReceiptForPurchaseOrderResponse> Value);
