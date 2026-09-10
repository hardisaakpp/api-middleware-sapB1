namespace Middleware.Application.Dtos.Requests;

public sealed record GetGoodsReceiptsByBaseEntryRequest(int BaseEntry, string VendorCode);
