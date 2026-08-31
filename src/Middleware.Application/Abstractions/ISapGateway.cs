using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.Abstractions;

public interface ISapGateway
{
    /// <summary>Transfer with per-line BaseType/BaseEntry/BaseLine (referencing a source document). Logs in against Sap:TestCompanyDb / Sap:TestPassword.</summary>
    public Task<DocumentCreatedResult> CreateStockTransferBasedOnDocumentAsync(CreateStockTransferBasedOnDocumentRequest request, CancellationToken ct = default);

    /// <summary>Plain warehouse-to-warehouse transfer, no base-document reference. Logs in against Sap:TestCompanyDb / Sap:TestPassword.</summary>
    public Task<DocumentCreatedResult> CreateStockTransferDirectAsync(CreateStockTransferDirectRequest request, CancellationToken ct = default);
    /// <summary>Looks up a StockTransfer by its U_sap_id correlation UDF. Returns null if no match exists in SAP.</summary>
    public Task<DocumentCreatedResult?> GetStockTransferByCorrelationIdAsync(string uSapId, CancellationToken ct = default);


    /// <summary>Transfer with per-line bin allocations (0, 1 or both sides, depending on which warehouse has bins enabled). Logs in against Sap:TestCompanyDb / Sap:TestPassword.</summary>
    public Task<DocumentCreatedResult> CreateStockTransferWithBinsAsync(CreateStockTransferWithBinsRequest request, CancellationToken ct = default);
    public Task<DocumentCreatedResult> CreateStockTransferRequestAsync(CreateStockTransferRequest request, CancellationToken ct = default);
    public Task<StockTransferStatusResponse?> GetStockTransferStatusAsync(int docEntry, CancellationToken ct = default);
    public Task<DocumentCreatedResult> CreateDepositAsync(CreateDepositRequest request, string companyDb, CancellationToken ct = default);
    public Task<DocumentCreatedResult> CreateGoodsReceiptAsync(CreateGoodsReceiptRequest request, CancellationToken ct = default);
    public Task<DocumentCreatedResult> CreateLiquidacionAsync(CreateLiquidacionRequest request, CancellationToken ct = default);
    public Task<DocumentCreatedResult> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default);
    public Task<PurchaseOrderResponse?> GetPurchaseOrderAsync(int docEntry, CancellationToken ct = default);
    public Task<PurchaseOrdersListResponse> GetPurchaseOrdersAsync(GetPurchaseOrdersRequest request, CancellationToken ct = default);
    public Task UpdatePurchaseOrderAsync(int docEntry, UpdatePurchaseOrderRequest request, CancellationToken ct = default);
    public Task<ItemResponse?> GetItemAsync(string itemCode, CancellationToken ct = default);

    /// <summary>Creates a SAP Item master record. Throws SapException with SAP's original status/body on failure (e.g. 400/-2035 duplicate ItemCode).</summary>
    public Task CreateItemAsync(CreateItemRequest request, CancellationToken ct = default);

    /// <summary>Partially updates a SAP Item master record — only the properties present in <paramref name="request"/> are sent.</summary>
    public Task UpdateItemAsync(string itemCode, UpdateItemRequest request, CancellationToken ct = default);

    /// <summary>Deletes a SAP Item master record. Throws SapException with SAP's original status/body on failure (e.g. 400 if the item has transactions and SAP refuses the delete).</summary>
    public Task DeleteItemAsync(string itemCode, CancellationToken ct = default);

    /// <summary>
    /// Fetches full item master data for the given codes in a single Service Layer call.
    /// Codes that do not exist in SAP are simply absent from the result.
    /// Callers must keep <paramref name="itemCodes"/> at or below the SL page size (20).
    /// </summary>
    public Task<IReadOnlyList<ItemResponse>> GetItemsByCodesAsync(IReadOnlyList<string> itemCodes, CancellationToken ct = default);
    public Task<ItemStockResponse?> GetItemStockAsync(string itemCode, string warehouseCode, CancellationToken ct = default);
    public Task<(IReadOnlyList<ItemStockResponse> Items, IReadOnlyList<string> NotFound)> GetItemsStockBatchAsync(string warehouseCode, IReadOnlyList<string> itemCodes, CancellationToken ct = default);
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

    /// <summary>Fetches the full SAP price list catalog (id + name only).</summary>
    public Task<IReadOnlyList<PriceListResponse>> GetPriceListsAsync(CancellationToken ct = default);

    /// <summary>
    /// True when the item master has positive stock, vendor orders, or customer commitments.
    /// A missing item (SAP 404) returns false.
    /// </summary>
    public Task<bool> HasItemMovementsAsync(string itemCode, CancellationToken ct = default);
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
    bool Locked,
    // Additive: needed so api-articulo-maestro's check_item_movements_in_sap parity check
    // (InStock/Committed/Ordered > 0) can be reproduced against this same per-warehouse data.
    decimal Ordered = 0m
);
