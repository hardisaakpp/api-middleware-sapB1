using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.Abstractions;

public interface ISapGateway
{
    /// <summary>Transfer with per-line BaseType/BaseEntry/BaseLine (referencing a source document). Logs in against Sap:TestCompanyDb / Sap:TestPassword.</summary>
    public Task<DocumentCreatedResult> CreateStockTransferBasedOnDocumentAsync(CreateStockTransferBasedOnDocumentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Misma forma de datos que <see cref="CreateStockTransferBasedOnDocumentAsync"/> (mismo
    /// request, per-line BaseType/BaseEntry/BaseLine opcional) pero autenticando contra
    /// Sap:CompanyDb / Sap:Password — la compañía REAL, no el sandbox de WebPOS. Para
    /// transferencias de bodega ajenas a WebPOS (ej. Distribución de api-cedi, P-011). No
    /// dispara la notificación WebPOS (esa integración es específica de la compañía de test).
    /// </summary>
    public Task<DocumentCreatedResult> CreateStockTransferForCompanyAsync(CreateStockTransferBasedOnDocumentRequest request, CancellationToken ct = default);

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

    /// <summary>Resolves a PurchaseOrder's DocEntry from its DocNum (Service Layer only indexes by DocEntry). Returns null if no match exists in SAP.</summary>
    public Task<int?> GetPurchaseOrderDocEntryByDocNumAsync(int docNum, CancellationToken ct = default);
    public Task<PurchaseOrdersListResponse> GetPurchaseOrdersAsync(GetPurchaseOrdersRequest request, CancellationToken ct = default);
    public Task UpdatePurchaseOrderAsync(int docEntry, UpdatePurchaseOrderRequest request, CancellationToken ct = default);
    public Task UpdatePurchaseOrderStatusAsync(int docEntry, string status, CancellationToken ct = default);
    public Task<ItemResponse?> GetItemAsync(string itemCode, CancellationToken ct = default);

    /// <summary>Creates a SAP Item master record. Throws SapException with SAP's original status/body on failure (e.g. 400/-2035 duplicate ItemCode).</summary>
    public Task CreateItemAsync(CreateItemRequest request, CancellationToken ct = default);

    /// <summary>Partially updates a SAP Item master record — only the properties present in <paramref name="request"/> are sent.</summary>
    public Task UpdateItemAsync(string itemCode, UpdateItemRequest request, CancellationToken ct = default);

    /// <summary>Deletes a SAP Item master record. Throws SapException with SAP's original status/body on failure (e.g. 400 if the item has transactions and SAP refuses the delete).</summary>
    public Task DeleteItemAsync(string itemCode, CancellationToken ct = default);

    /// <summary>
    /// Busca ítems por ItemCode o BarCode (contains, no exacto — para tipear parcial o
    /// escanear un código de barras). Para P-012 de api-cedi ("Consulta Stock").
    /// </summary>
    public Task<IReadOnlyList<ItemSearchResult>> SearchItemsAsync(string query, int top, CancellationToken ct = default);

    /// <summary>
    /// Stock del ítem en TODOS los almacenes de una vez (vía ItemWarehouseInfoCollection,
    /// mismo dato que ya usa GetItemStockAsync pero sin filtrar a un solo almacén) — evita
    /// N llamadas (una por almacén) para "mostrar por ubicación" en P-012. Null si el
    /// ItemCode no existe en SAP; lista vacía si existe pero no tiene ningún almacén con
    /// InStock/Committed distinto de cero.
    /// </summary>
    public Task<IReadOnlyList<ItemStockResponse>?> GetItemStockAllWarehousesAsync(string itemCode, CancellationToken ct = default);

    /// <summary>
    /// Fetches full item master data for the given codes in a single Service Layer call.
    /// Codes that do not exist in SAP are simply absent from the result.
    /// Callers must keep <paramref name="itemCodes"/> at or below the SL page size (20).
    /// </summary>
    public Task<IReadOnlyList<ItemResponse>> GetItemsByCodesAsync(IReadOnlyList<string> itemCodes, CancellationToken ct = default);
    public Task<ItemStockResponse?> GetItemStockAsync(string itemCode, string warehouseCode, CancellationToken ct = default);

    /// <summary>
    /// Pre-loads the ItemGroups map into its cache so the first real item lookup does not
    /// pay the one-time cost of paging the full ItemGroups collection from Service Layer.
    /// Safe to call at startup: never throws (a failed load leaves the cache cold so the
    /// next item request simply retries).
    /// </summary>
    public Task PrewarmItemGroupsAsync(CancellationToken ct = default);
    public Task<(IReadOnlyList<ItemStockResponse> Items, IReadOnlyList<string> NotFound)> GetItemsStockBatchAsync(string warehouseCode, IReadOnlyList<string> itemCodes, CancellationToken ct = default);
    public Task<BusinessPartnerResponse?> GetBusinessPartnerAsync(string cardCode, CancellationToken ct = default);
    public Task<InventoryTransferRequestsListResponse> GetInventoryTransferRequestsByCommentAsync(string commentsContain, CancellationToken ct = default);
    public Task UpdateInventoryTransferRequestStatusAsync(int docEntry, string status, CancellationToken ct = default);

    /// <summary>Listado paginado crudo, más nuevo primero (mismo patrón que GetPurchaseOrdersAsync) — para P-008 de api-cedi.</summary>
    public Task<InventoryTransferRequestsRawListResponse> GetInventoryTransferRequestsAsync(GetInventoryTransferRequestsRequest request, CancellationToken ct = default);

    /// <summary>GET individual por DocEntry, tipado (incluye RemainingOpenQuantity por línea) — para "actualizar una SOT puntual".</summary>
    public Task<InventoryTransferRequestResponse?> GetInventoryTransferRequestAsync(int docEntry, CancellationToken ct = default);

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

    /// <summary>
    /// Same data as <see cref="GetItemStockAsync"/> but logs in against Sap:TestCompanyDb /
    /// Sap:TestPassword — required to read the post-transfer stock of a StockTransfer created by
    /// CreateStockTransferDirectAsync/BasedOnDocumentAsync/WithBinsAsync, which all run in the
    /// test company, never in Sap:CompanyDb.
    /// </summary>
    public Task<ItemStockResponse?> GetItemStockForTestCompanyAsync(string itemCode, string warehouseCode, CancellationToken ct = default);

    /// <summary>
    /// Resolves, for each given warehouse code, whether it participates in WEBPOS (UDF
    /// <c>U_WEBPOS</c>) and which WEBPOS "negocio" (UDF <c>U_NEGOCIO</c>) it belongs to. Logs in
    /// against Sap:TestCompanyDb / Sap:TestPassword, matching the company used to create the
    /// StockTransfer this is enriching. Codes not present in SAP are simply absent from the result.
    /// </summary>
    public Task<IReadOnlyDictionary<string, WarehousePosInfo>> GetWarehousesPosInfoForTestCompanyAsync(
        IReadOnlyList<string> warehouseCodes, CancellationToken ct = default);

    /// <summary>
    /// Fetches an existing StockTransfer by DocEntry (lines + warehouses) from Sap:TestCompanyDb —
    /// used to rebuild the WEBPOS notification payload on a manual retry, without keeping our own
    /// copy of the transfer that could drift from SAP.
    /// </summary>
    public Task<StockTransferDocumentResponse?> GetStockTransferForTestCompanyAsync(int docEntry, CancellationToken ct = default);

    /// <summary>
    /// Master data (price list 1, brand, tax code, barcodes) WEBPOS needs to auto-create an
    /// item/store pair the first time it sees it — used only on the retry after WEBPOS answers
    /// "El producto no existe.". Logs in against Sap:TestCompanyDb / Sap:TestPassword.
    /// </summary>
    public Task<ItemPosEnrichment?> GetItemPosEnrichmentForTestCompanyAsync(string itemCode, CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="GetWarehousesPosInfoForTestCompanyAsync"/> but against the default
    /// company (Sap:CompanyDb) — required to enrich documents created there, like goods receipts
    /// (PurchaseDeliveryNotes), whose WEBPOS notification must read POS flags from the same
    /// company the document lives in.
    /// </summary>
    public Task<IReadOnlyDictionary<string, WarehousePosInfo>> GetWarehousesPosInfoAsync(
        IReadOnlyList<string> warehouseCodes, CancellationToken ct = default);

    /// <summary>Same as <see cref="GetItemPosEnrichmentForTestCompanyAsync"/> but against the default company (Sap:CompanyDb).</summary>
    public Task<ItemPosEnrichment?> GetItemPosEnrichmentAsync(string itemCode, CancellationToken ct = default);

    /// <summary>
    /// Fetches an existing goods receipt (PurchaseDeliveryNote) by DocEntry — lines with their
    /// receiving warehouse — from the default company (Sap:CompanyDb). Used to rebuild the WEBPOS
    /// notification payload on a manual retry, mirroring <see cref="GetStockTransferForTestCompanyAsync"/>.
    /// </summary>
    public Task<GoodsReceiptDocumentResponse?> GetGoodsReceiptAsync(int docEntry, CancellationToken ct = default);

    /// <summary>Fetches the full SAP price list catalog (id + name only).</summary>
    public Task<IReadOnlyList<PriceListResponse>> GetPriceListsAsync(CancellationToken ct = default);

    /// <summary>
    /// True when the item master has positive stock, vendor orders, or customer commitments.
    /// A missing item (SAP 404) returns false.
    /// </summary>
    public Task<bool> HasItemMovementsAsync(string itemCode, CancellationToken ct = default);
}

public sealed record GoodsReceiptDocumentResponse(
    int DocEntry,
    int DocNum,
    IReadOnlyList<GoodsReceiptDocumentLine> Lines
);

public sealed record GoodsReceiptDocumentLine(string ItemCode, decimal Quantity, string WarehouseCode);

public sealed record WarehousePosInfo(string WarehouseCode, bool IsPosEnabled, string? CodBusiness);

public sealed record ItemPosEnrichment(string? Price, string? Brand, string? CodeTax, IReadOnlyList<string> Barcodes);

public sealed record StockTransferDocumentResponse(
    int DocEntry,
    int DocNum,
    string FromWarehouse,
    string ToWarehouse,
    IReadOnlyList<StockTransferDocumentLine> Lines
);

public sealed record StockTransferDocumentLine(string ItemCode, decimal Quantity, string WarehouseCode);

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
