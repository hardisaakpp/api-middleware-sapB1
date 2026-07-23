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
    public Task<DocumentCreatedResult> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default);
    public Task<PurchaseOrderResponse?> GetPurchaseOrderAsync(int docEntry, CancellationToken ct = default);
    public Task<PurchaseOrdersListResponse> GetPurchaseOrdersAsync(GetPurchaseOrdersRequest request, CancellationToken ct = default);
    public Task UpdatePurchaseOrderAsync(int docEntry, UpdatePurchaseOrderRequest request, CancellationToken ct = default);
    public Task<ItemResponse?> GetItemAsync(string itemCode, CancellationToken ct = default);

    /// <summary>
    /// Fetches full item master data for the given codes in a single Service Layer call.
    /// Codes that do not exist in SAP are simply absent from the result.
    /// Callers must keep <paramref name="itemCodes"/> at or below the SL page size (20).
    /// </summary>
    public Task<IReadOnlyList<ItemResponse>> GetItemsByCodesAsync(IReadOnlyList<string> itemCodes, CancellationToken ct = default);
    public Task<ItemStockResponse?> GetItemStockAsync(string itemCode, string warehouseCode, CancellationToken ct = default);
    public Task<BusinessPartnerResponse?> GetBusinessPartnerAsync(string cardCode, CancellationToken ct = default);
    public Task<InventoryTransferRequestsListResponse> GetInventoryTransferRequestsByCommentAsync(string commentsContain, CancellationToken ct = default);
    public Task UpdateInventoryTransferRequestStatusAsync(int docEntry, string status, CancellationToken ct = default);

    /// <summary>
    /// Fetches items with their per-warehouse stock info in a single Service Layer call.
    /// Callers must keep <paramref name="itemCodes"/> at or below the SL page size (20)
    /// so results are never truncated by server-side paging.
    /// </summary>
    public Task<IReadOnlyList<SapItemValidationData>> GetItemsForValidationAsync(IReadOnlyList<string> itemCodes, CancellationToken ct = default);

    /// <summary>Fetches the full warehouse catalog (follows Service Layer paging internally).</summary>
    public Task<IReadOnlyList<WarehouseResponse>> GetWarehousesAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetches only the given warehouse codes in a single Service Layer call.
    /// Callers must keep <paramref name="warehouseCodes"/> at or below the SL page size (20).
    /// </summary>
    public Task<IReadOnlyList<WarehouseResponse>> GetWarehousesByCodesAsync(IReadOnlyList<string> warehouseCodes, CancellationToken ct = default);
}

public sealed record DocumentCreatedResult(int DocEntry, int DocNum);

public sealed record SapItemValidationData(
    string ItemCode,
    string ItemName,
    bool PurchaseItem,
    bool Valid,
    bool Frozen,
    string? InventoryUoM,
    IReadOnlyList<SapItemWarehouseData> Warehouses
);

public sealed record SapItemWarehouseData(
    string WarehouseCode,
    decimal InStock,
    decimal Committed,
    bool Locked
);
