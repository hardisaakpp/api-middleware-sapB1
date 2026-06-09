using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.Abstractions;

public interface ISapGateway
{
    public Task<DocumentCreatedResult> CreateStockTransferAsync(CreateStockTransferRequest request, CancellationToken ct = default);
    public Task<DocumentCreatedResult> CreateStockTransferWithBinsAsync(CreateStockTransferWithBinsRequest request, CancellationToken ct = default);
    public Task<DocumentCreatedResult> CreateStockTransferRequestAsync(CreateStockTransferRequest request, CancellationToken ct = default);
    public Task<DocumentCreatedResult> CreateDepositAsync(CreateDepositRequest request, string companyDb, CancellationToken ct = default);
    public Task<DocumentCreatedResult> CreateGoodsReceiptAsync(CreateGoodsReceiptRequest request, CancellationToken ct = default);
    public Task<DocumentCreatedResult> CreateLiquidacionAsync(CreateLiquidacionRequest request, CancellationToken ct = default);
    public Task<PurchaseOrderResponse?> GetPurchaseOrderAsync(int docEntry, CancellationToken ct = default);
    public Task UpdatePurchaseOrderAsync(int docEntry, UpdatePurchaseOrderRequest request, CancellationToken ct = default);
}

public sealed record DocumentCreatedResult(int DocEntry, int DocNum);
