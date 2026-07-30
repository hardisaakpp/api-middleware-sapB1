using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Infrastructure.Configuration;
using Polly.Timeout;

namespace Middleware.Infrastructure.Sap;

internal sealed class SapGateway : ISapGateway
{
    private readonly ISapSessionManager _sessionManager;
    private readonly ISapServiceLayerClient _client;
    private readonly SapThrottle _throttle;
    private readonly ItemGroupsCache _itemGroupsCache;
    private readonly SapSettings _settings;
    private readonly ILogger<SapGateway> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string ValueProperty = "value";
    private const string ItemCodeProperty = "ItemCode";
    private const string WarehouseCodeProperty = "WarehouseCode";

    public SapGateway(
        ISapSessionManager sessionManager,
        ISapServiceLayerClient client,
        SapThrottle throttle,
        ItemGroupsCache itemGroupsCache,
        IOptions<SapSettings> settings,
        ILogger<SapGateway> logger)
    {
        _sessionManager = sessionManager;
        _client = client;
        _throttle = throttle;
        _itemGroupsCache = itemGroupsCache;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<DocumentCreatedResult> CreateStockTransferTestAsync(CreateStockTransferTestRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.TestCompanyDb))
        {
            throw new InvalidOperationException("Sap:TestCompanyDb no está configurado.");
        }

        if (string.IsNullOrWhiteSpace(_settings.TestPassword))
        {
            throw new InvalidOperationException("Sap:TestPassword no está configurado.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            request.DocObjectCode,
            request.CardCode,
            request.DocDate, // NOSONAR
            request.TaxDate,
            request.Comments,
            FromWarehouse = request.FromWarehouse, // NOSONAR
            ToWarehouse = request.ToWarehouse, // NOSONAR
            request.U_ORIGEN,
            request.Series,
            // PriceList -2 = "Último precio determinado" (config SAP) si el cliente no lo especifica.
            PriceList = request.PriceList ?? -2,
            StockTransferLines = request.StockTransferLines.Select(l => new
            {
                l.BaseType,
                l.BaseEntry,
                l.BaseLine,
                l.ItemCode,
                l.Quantity,
                l.WarehouseCode
            })
        }, JsonOptions);

        var json = await ExecuteWithRetryOn401Async(
            HttpMethod.Post, "StockTransfers", payload, ct, _settings.TestCompanyDb, _settings.TestPassword);
        return ParseDocumentResult(json);
    }

    public async Task<DocumentCreatedResult> CreateStockTransferRequestAsync(CreateStockTransferRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            DocDate = request.DocDate.ToString("yyyy-MM-dd"),
            request.Comments,
            // PriceList -2 = "Último precio determinado" (config SAP). El flujo Python que
            // funcionaba lo envía, así que se replica para paridad de payload. No quitar.
            PriceList = -2,
            FromWarehouse = request.FromWarehouse,
            ToWarehouse = request.ToWarehouse,
            // U_ODC (UDF con el nº de OC de origen) y Series los incluye el payload del flujo
            // Python que sí funciona. U_ODC es el candidato más probable del error 1299
            // "no data found" de la TN custom (hace lookup contra la OC). Se omiten solos si
            // el request no los trae (WhenWritingNull).
            U_ODC = request.UOdc,
            Series = request.Series,
            // ITR: la bodega va SOLO en la cabecera (FromWarehouse/ToWarehouse). A nivel de
            // línea solo ItemCode y Quantity, replicando exactamente el payload del flujo
            // Python que sí funcionaba. Los campos de bodega por línea (FromWarehouseCode/
            // WarehouseCode, válidos en StockTransfers) NO se envían para un InventoryTransferRequest.
            StockTransferLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                l.ProjectCode
            })
        }, JsonOptions);

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, "InventoryTransferRequests", payload, ct);
        return ParseDocumentResult(json);
    }

    public async Task<DocumentCreatedResult> CreateDepositAsync(CreateDepositRequest request, string companyDb, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            DocDate = request.DocDate.ToString("yyyy-MM-dd"),
            request.Comments,
            DocumentLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                WarehouseCode = l.WarehouseCode // NOSONAR
            })
        }, JsonOptions);

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, "InventoryGenEntries", payload, ct);
        return ParseDocumentResult(json);
    }

    public async Task<DocumentCreatedResult> CreateGoodsReceiptAsync(CreateGoodsReceiptRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            CardCode = request.VendorCode,
            DocDate = request.DocDate.ToString("yyyy-MM-dd"),
            request.Comments,
            DocumentLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                l.UnitPrice,
                WarehouseCode = l.WarehouseCode // NOSONAR
            })
        }, JsonOptions);

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, "PurchaseDeliveryNotes", payload, ct);
        return ParseDocumentResult(json);
    }

    public async Task<DocumentCreatedResult> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            CardCode  = request.CardCode, // NOSONAR
            DocDate   = request.DocDate.ToString("yyyy-MM-dd"),
            DocDueDate= request.DocDueDate.ToString("yyyy-MM-dd"),
            TaxDate   = request.TaxDate.ToString("yyyy-MM-dd"),
            request.Comments,
            U_ORIGEN  = request.U_ORIGEN, // NOSONAR
            U_USUARIO = request.U_USUARIO, // NOSONAR
            U_FECHA   = request.U_FECHA?.ToString("yyyy-MM-dd"),
            U_FE_ETG  = request.U_FE_ETG?.ToString("yyyy-MM-dd"),
            U_LOTE    = request.U_LOTE, // NOSONAR
            DocumentLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                l.UnitPrice,
                WarehouseCode = l.WarehouseCode // NOSONAR
            })
        }, JsonOptions);

        try
        {
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, "PurchaseOrders", payload, ct);
            return ParseDocumentResult(json);
        }
        catch (TimeoutRejectedException) when (!string.IsNullOrEmpty(request.Comments))
        {
            _logger.LogWarning( // NOSONAR
                "SAP POST PurchaseOrders timed out for CardCode={CardCode}, Comments='{Comments}'. Checking for orphaned order...",
                request.CardCode, request.Comments);

            var recovered = await TryRecoverOrphanedOrderAsync(request.CardCode, request.Comments, ct);
            if (recovered is not null)
            {
                _logger.LogWarning(
                    "Recovered orphaned PurchaseOrder: DocEntry={DocEntry}, DocNum={DocNum}, Comments='{Comments}'",
                    recovered.DocEntry, recovered.DocNum, request.Comments);
                return recovered;
            }

            throw;
        }
    }

    private async Task<DocumentCreatedResult?> TryRecoverOrphanedOrderAsync(string cardCode, string comments, CancellationToken ct)
    {
        try
        {
            var escapedComments = comments.Replace("'", "''");
            var resource = $"PurchaseOrders?$filter=CardCode eq '{cardCode}' and Comments eq '{escapedComments}'&$select=DocEntry,DocNum&$orderby=DocEntry desc&$top=1";

            var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(ValueProperty, out var arr))
            {
                return null;
            }

            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("DocEntry", out var de) && de.TryGetInt32(out var docEntry) && // NOSONAR
                    item.TryGetProperty("DocNum",  out var dn) && dn.TryGetInt32(out var docNum)) // NOSONAR
                {
                    return new DocumentCreatedResult(docEntry, docNum);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not query SAP for orphaned order — CardCode={CardCode}, Comments='{Comments}'",
                cardCode, comments);
            return null;
        }
    }

    // Ported 1:1 from API_SLAYER's SapService.CreatePurchaseLiquidationAsync — same SAP resource
    // (PurchaseInvoices), same field defaults, same server-stamped dates and AccountCode fixup.
    public async Task<DocumentCreatedResult> CreateLiquidacionAsync(CreateLiquidacionRequest request, CancellationToken ct = default)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd"); // NOSONAR

        var payload = new Dictionary<string, object?>
        {
            ["CardCode"] = request.CardCode,
            ["DocType"] = NormalizeLiquidacionText(request.DocType) ?? "dDocument_Service",
            ["DocDate"] = today,
            ["DocDueDate"] = today,
            ["NumAtCard"] = string.Empty,
            ["Comments"] = NormalizeLiquidacionText(request.Comments) ?? "LIQUIDACION DE COMPRA - API",
            ["U_COD_ST"] = NormalizeLiquidacionText(request.U_COD_ST) ?? "01",
            ["U_tipo_comprob"] = NormalizeLiquidacionText(request.U_tipo_comprob) ?? "03",
            ["U_ORIGEN"] = NormalizeLiquidacionText(request.U_ORIGEN) ?? "C",
            ["U_SER_EST"] = NormalizeLiquidacionText(request.U_SER_EST) ?? "001",
            ["U_SER_PE"] = NormalizeLiquidacionText(request.U_SER_PE) ?? "100"
        };

        if (request.Series.HasValue)
        {
            payload["Series"] = request.Series.Value;
        }

        var resolvedDocCurrency = NormalizeLiquidacionText(request.DocCurrency);
        if (!string.IsNullOrWhiteSpace(resolvedDocCurrency))
        {
            payload["DocCurrency"] = resolvedDocCurrency;
        }

        var resolvedJournalMemo = NormalizeLiquidacionText(request.JournalMemo);
        if (!string.IsNullOrWhiteSpace(resolvedJournalMemo))
        {
            payload["JournalMemo"] = resolvedJournalMemo;
        }

        if (request.U_STATUS.HasValue)
        {
            payload["U_STATUS"] = request.U_STATUS.Value;
        }

        payload["DocumentLines"] = (request.DocumentLines ?? []).Select(line =>
        {
            var node = new Dictionary<string, object?>
            {
                ["AccountCode"] = NormalizeLiquidacionAccountCode(line.AccountCode),
                ["ItemDescription"] = line.ItemDescription,
                ["Quantity"] = line.Quantity,
                ["UnitPrice"] = line.UnitPrice,
                ["TaxCode"] = line.TaxCode,
                ["CostingCode"] = line.CostingCode,
                ["CostingCode2"] = line.CostingCode2,
                ["CostingCode3"] = line.CostingCode3
            };

            if (!string.IsNullOrWhiteSpace(line.CostingCode4))
            {
                node["CostingCode4"] = line.CostingCode4;
            }

            if (!string.IsNullOrWhiteSpace(line.CostingCode5))
            {
                node["CostingCode5"] = line.CostingCode5;
            }

            return node;
        }).ToList();

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        _logger.LogInformation("JSON final liquidacion antes de enviar a SAP:\n{Payload}", payloadJson); // NOSONAR

        try
        {
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, "PurchaseInvoices", payloadJson, ct);
            return ParseDocumentResult(json);
        }
        catch (SapException ex) when (IsLiquidacionAccountCodeError(ex.SapError))
        {
            throw new SapException(ex.StatusCode,
                ex.SapError + " | Verifique que AccountCode corresponda al codigo interno OACT.AcctCode (ej: _SYS...).");
        }
    }

    // "string" / "__TODAY__" / "__DOCNO__" are Swagger/legacy-client placeholder values, treated as absent.
    private static string? NormalizeLiquidacionText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals("string", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("__TODAY__", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("__DOCNO__", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    // SAP B1 with segmented accounts often uses internal AcctCode like _SYS00000000000.
    private static string NormalizeLiquidacionAccountCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("SYS", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("_SYS", StringComparison.OrdinalIgnoreCase))
        {
            return $"_{trimmed}";
        }

        return trimmed;
    }

    private static bool IsLiquidacionAccountCodeError(string sapError)
    {
        try
        {
            var errorObj = JsonSerializer.Deserialize<JsonElement>(sapError);
            var code = errorObj.TryGetProperty("error", out var errorProp) && errorProp.TryGetProperty("code", out var codeProp)
                ? codeProp.ToString() : "";
            var message = errorObj.TryGetProperty("error", out var errorProp2) && errorProp2.TryGetProperty("message", out var msgProp) && msgProp.TryGetProperty(ValueProperty, out var valProp)
                ? valProp.ToString() : "";
            return code == "-2028" && message.Contains("DocumentLines.AccountCode", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<PurchaseOrderResponse?> GetPurchaseOrderAsync(int docEntry, CancellationToken ct = default)
    {
        string json;
        try
        {
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get, $"PurchaseOrders({docEntry})", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP PurchaseOrder DocEntry={DocEntry} not found (SAP 404)", docEntry); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int RequiredInt(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.TryGetInt32(out var v))
            {
                return v;
            }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de PurchaseOrder DocEntry={docEntry}");
        }
        string RequiredString(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String)
            {
                return p.GetString()!;
            }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de PurchaseOrder DocEntry={docEntry}");
        }
        decimal RequiredDecimal(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.TryGetDecimal(out var v))
            {
                return v;
            }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de PurchaseOrder DocEntry={docEntry}");
        }

        var lines = root.TryGetProperty("DocumentLines", out var linesEl)
            ? linesEl.EnumerateArray()
                .Select((l, i) => new PurchaseOrderLineResponse(
                    RequiredInt(l, "LineNum", $"DocumentLines[{i}]"),
                    RequiredString(l, ItemCodeProperty, $"DocumentLines[{i}]"),
                    RequiredDecimal(l, "Quantity", $"DocumentLines[{i}]"),
                    RequiredDecimal(l, "OpenQuantity", $"DocumentLines[{i}]"),
                    RequiredDecimal(l, "UnitPrice", $"DocumentLines[{i}]"),
                    RequiredString(l, WarehouseCodeProperty, $"DocumentLines[{i}]")
                )).ToList()
            : (IReadOnlyList<PurchaseOrderLineResponse>)[];

        return new PurchaseOrderResponse(
            RequiredInt(root, "DocEntry", "root"),
            RequiredInt(root, "DocNum", "root"),
            RequiredString(root, "CardCode", "root"),
            RequiredString(root, "DocDate", "root"),
            RequiredString(root, "DocumentStatus", "root"),
            lines
        );
    }

    public async Task<PurchaseOrdersListResponse> GetPurchaseOrdersAsync(GetPurchaseOrdersRequest request, CancellationToken ct = default)
    {
        var resource = $"PurchaseOrders?$top={request.Top}&$skip={request.Skip}&$orderby=DocEntry desc";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        var items = node?[ValueProperty]?.AsArray()
            .OfType<System.Text.Json.Nodes.JsonObject>()
            .ToList()
            ?? [];

        return new PurchaseOrdersListResponse(items, items.Count);
    }

    public async Task UpdatePurchaseOrderAsync(int docEntry, UpdatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            DocumentLines = request.Lines.Select(l => new
            {
                l.LineNum,
                l.Quantity,
                l.WarehouseCode
            })
        }, JsonOptions);

        try
        {
            await ExecuteWithRetryOn401Async(new HttpMethod("PATCH"), $"PurchaseOrders({docEntry})", payload, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP PurchaseOrder DocEntry={DocEntry} not found (SAP 404)", docEntry); // NOSONAR
            throw;
        }
    }

    // InventoryUoMEntry is the numeric UoM entry id (e.g. -1); the human-readable UoM code
    // used across this API's "uomCode"/InventoryUoM fields is the string field InventoryUOM
    // (e.g. "UNIDAD") — confirmed against real Service Layer data.
    private const string ItemSelectFields =
        "ItemCode,ItemName,InventoryUOM,QuantityOnStock,PurchaseItem,SalesItem,InventoryItem," +
        "Valid,Frozen,ItemType,ItemsGroupCode,Mainsupplier";

    public async Task<ItemResponse?> GetItemAsync(string itemCode, CancellationToken ct = default)
    {
        string json;
        var encoded = Uri.EscapeDataString(itemCode);
        try
        {
            try
            {
                json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                    $"Items('{encoded}')?$select={ItemSelectFields},U_IXX_PACK", null, ct);
            }
            catch (SapException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                // Company DBs without the U_IXX_PACK UDF reject the $select; retry without it
                _logger.LogWarning(ex, "SAP rejected $select with U_IXX_PACK for Item '{ItemCode}'. Retrying without the UDF.", itemCode);
                json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                    $"Items('{encoded}')?$select={ItemSelectFields}", null, ct);
            }
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP Item '{ItemCode}' not found", itemCode); // NOSONAR
            return null;
        }

        var groups = await GetItemGroupsMapAsync(ct);

        using var doc = JsonDocument.Parse(json);
        return ParseItem(doc.RootElement, itemCode, groups);
    }

    public async Task<IReadOnlyList<ItemResponse>> GetItemsByCodesAsync(
        IReadOnlyList<string> itemCodes, CancellationToken ct = default)
    {
        if (itemCodes.Count == 0)
        {
            return [];
        }

        var filter = string.Join(" or ", itemCodes.Select(c => $"ItemCode eq '{c.Replace("'", "''")}'"));
        string json;
        try
        {
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items?$filter={filter}&$select={ItemSelectFields},U_IXX_PACK", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            _logger.LogWarning(ex, "SAP rejected $select with U_IXX_PACK for items batch. Retrying without the UDF.");
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items?$filter={filter}&$select={ItemSelectFields}", null, ct);
        }

        var groups = await GetItemGroupsMapAsync(ct);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(ValueProperty, out var arr))
        {
            return [];
        }

        var items = new List<ItemResponse>();
        foreach (var element in arr.EnumerateArray())
        {
            if (element.TryGetProperty(ItemCodeProperty, out var ic) && ic.ValueKind == JsonValueKind.String)
            {
                items.Add(ParseItem(element, ic.GetString()!, groups));
            }
        }
        return items;
    }

    private static ItemResponse ParseItem(
        JsonElement root, string fallbackItemCode, IReadOnlyDictionary<int, string> groups)
    {
        string? StrOpt(string field) =>
            root.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        // SL sometimes returns "" instead of null for unset master-data fields
        string? StrOptNormalized(string field) =>
            StrOpt(field) is { } s && !string.IsNullOrWhiteSpace(s) ? s : null;

        int? groupCode =
            root.TryGetProperty("ItemsGroupCode", out var gc) && gc.ValueKind == JsonValueKind.Number && gc.TryGetInt32(out var gcVal)
                ? gcVal
                : null;

        return new ItemResponse(
            StrOpt(ItemCodeProperty) ?? fallbackItemCode,
            StrOpt("ItemName") ?? string.Empty,
            StrOpt("InventoryUOM"),
            GetDecimalOrZero(root, "QuantityOnStock"),
            IsTyes(root, "PurchaseItem"),
            IsTyes(root, "SalesItem"),
            IsTyes(root, "InventoryItem"),
            IsTyes(root, "Valid"),
            IsTyes(root, "Frozen"),
            StrOpt("ItemType"),
            StrOptNormalized("U_IXX_PACK"),
            groupCode,
            groupCode is int g && groups.TryGetValue(g, out var groupName) ? groupName : null,
            StrOptNormalized("Mainsupplier")
        );
    }

    private async Task<IReadOnlyDictionary<int, string>> GetItemGroupsMapAsync(CancellationToken ct)
    {
        try
        {
            return await _itemGroupsCache.GetOrLoadAsync(FetchItemGroupsAsync, ct);
        }
        catch (Exception ex)
        {
            // Group-name enrichment must never fail an item lookup
            _logger.LogWarning(ex, "Could not load SAP ItemGroups; groupName will be null for this request");
            return new Dictionary<int, string>();
        }
    }

    private async Task<IReadOnlyDictionary<int, string>> FetchItemGroupsAsync(CancellationToken ct)
    {
        var map = new Dictionary<int, string>();
        string? resource = "ItemGroups?$select=Number,GroupName";

        // SL pages results (default 20 per page); follow odata.nextLink until exhausted
        const int maxPages = 100;
        for (var page = 0; resource is not null && page < maxPages; page++)
        {
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            AddItemGroupsFromPage(root, map);
            resource = GetNextLink(root);
        }

        _logger.LogInformation("Loaded {Count} SAP ItemGroups into cache", map.Count);
        return map;
    }

    private static void AddItemGroupsFromPage(JsonElement root, Dictionary<int, string> map)
    {
        if (!root.TryGetProperty(ValueProperty, out var arr))
        {
            return;
        }

        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.TryGetProperty("Number", out var num) && num.ValueKind == JsonValueKind.Number &&
                num.TryGetInt32(out var code) &&
                entry.TryGetProperty("GroupName", out var gn) && gn.ValueKind == JsonValueKind.String)
            {
                map[code] = gn.GetString()!;
            }
        }
    }

    // Shared odata.nextLink resolution for the two paginated SAP resources this gateway follows.
    private static string? GetNextLink(JsonElement root)
    {
        if (root.TryGetProperty("odata.nextLink", out var next) && next.ValueKind == JsonValueKind.String)
        {
            return next.GetString();
        }

        return root.TryGetProperty("@odata.nextLink", out var next4) && next4.ValueKind == JsonValueKind.String
            ? next4.GetString()
            : null;
    }

    public async Task<ItemStockResponse?> GetItemStockAsync(string itemCode, string warehouseCode, CancellationToken ct = default)
    {
        string json;
        try
        {
            var encoded = Uri.EscapeDataString(itemCode);
            // Fetch the item with its per-warehouse collection and filter in-memory; // NOSONAR
            // SAP SL does not support $filter inside nested collections via OData.
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items('{encoded}')?$select=ItemCode,InventoryUOM,ItemWarehouseInfoCollection", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP Item '{ItemCode}' not found when querying stock", itemCode); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("ItemWarehouseInfoCollection", out var collection))
        {
            return null;
        }

        // Item-level UoM (same field GetItemsForValidationAsync exposes as SapItemValidationData.InventoryUoM)
        var uomCode = root.TryGetProperty("InventoryUOM", out var uom) && uom.ValueKind == JsonValueKind.String
            ? uom.GetString()
            : null;

        foreach (var entry in collection.EnumerateArray())
        {
            if (!entry.TryGetProperty(WarehouseCodeProperty, out var wc) || wc.GetString() != warehouseCode)
            {
                continue;
            }

            // Same resolution as GetItemsForValidationAsync's SapItemWarehouseData, so both
            // routes report identical InStock/Committed/Locked for the same item+warehouse.
            return new ItemStockResponse(
                itemCode, warehouseCode,
                GetDecimalOrZero(entry, "InStock"),
                GetDecimalOrZero(entry, "Committed"),
                uomCode,
                IsTyes(entry, "Locked"));
        }

        // Warehouse found in SAP catalog but no entry for this warehouse → stock 0
        return new ItemStockResponse(itemCode, warehouseCode, 0m, 0m, uomCode, Locked: false);
    }

    public async Task<BusinessPartnerResponse?> GetBusinessPartnerAsync(string cardCode, CancellationToken ct = default)
    {
        string json;
        try
        {
            var encoded = Uri.EscapeDataString(cardCode);
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"BusinessPartners('{encoded}')?$select=CardCode,CardName,CardType,Currency,Phone1,EmailAddress,Valid,Frozen", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP BusinessPartner '{CardCode}' not found", cardCode); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string Str(string field) =>
            root.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : string.Empty;
        string? StrOpt(string field) =>
            root.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        return new BusinessPartnerResponse(
            Str("CardCode"),
            Str("CardName"),
            Str("CardType"),
            StrOpt("Currency"),
            StrOpt("Phone1"),
            StrOpt("EmailAddress"),
            Valid:  IsTyes(root, "Valid"),
            Frozen: IsTyes(root, "Frozen")
        );
    }

    public async Task<InventoryTransferRequestsListResponse> GetInventoryTransferRequestsByCommentAsync( // NOSONAR
        string commentsContain, CancellationToken ct = default)
    {
        var escaped = commentsContain.Replace("'", "''");
        var resource = $"InventoryTransferRequests?$filter=contains(Comments,'{escaped}')&$select=DocEntry,DocNum,Comments,FromWarehouse,ToWarehouse,DocDate,DocumentStatus,StockTransferLines";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty(ValueProperty, out var arr))
        {
            return new InventoryTransferRequestsListResponse([]);
        }

        var items = arr.EnumerateArray().Select(item =>
        {
            int GetInt(string f) => item.TryGetProperty(f, out var p) && p.TryGetInt32(out var v) ? v : 0;
            string GetStr(string f) => item.TryGetProperty(f, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : string.Empty;
            string? GetStrOpt(string f) => item.TryGetProperty(f, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

            var lines = item.TryGetProperty("StockTransferLines", out var linesEl)
                ? linesEl.EnumerateArray().Select((l, i) => new InventoryTransferRequestLineResponse(
                    l.TryGetProperty("LineNum", out var ln) && ln.TryGetInt32(out var lv) ? lv : i,
                    l.TryGetProperty(ItemCodeProperty, out var li) && li.ValueKind == JsonValueKind.String ? li.GetString()! : string.Empty,
                    l.TryGetProperty("Quantity", out var lq) && lq.TryGetDecimal(out var lqv) ? lqv : 0m,
                    l.TryGetProperty(WarehouseCodeProperty, out var lw) && lw.ValueKind == JsonValueKind.String ? lw.GetString() : null
                )).ToList()
                : (IReadOnlyList<InventoryTransferRequestLineResponse>)[];

            return new InventoryTransferRequestResponse(
                GetInt("DocEntry"),
                GetInt("DocNum"),
                GetStrOpt("Comments"),
                GetStr("FromWarehouse"),
                GetStr("ToWarehouse"),
                GetStr("DocDate"),
                GetStr("DocumentStatus"),
                lines
            );
        }).ToList();

        return new InventoryTransferRequestsListResponse(items);
    }

    public async Task UpdateInventoryTransferRequestStatusAsync(int docEntry, string status, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            U_STATUS = status // NOSONAR
        }, JsonOptions);

        try
        {
            await ExecuteWithRetryOn401Async(new HttpMethod("PATCH"), $"InventoryTransferRequests({docEntry})", payload, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP InventoryTransferRequest DocEntry={DocEntry} not found (SAP 404)", docEntry); // NOSONAR
            throw;
        }
    }

    public async Task<IReadOnlyList<SapItemValidationData>> GetItemsForValidationAsync(
        IReadOnlyList<string> itemCodes, CancellationToken ct = default)
    {
        if (itemCodes.Count == 0)
        {
            return [];
        }

        var filter = string.Join(" or ", itemCodes.Select(c => $"ItemCode eq '{c.Replace("'", "''")}'"));
        var resource = $"Items?$filter={filter}" +
                       "&$select=ItemCode,ItemName,PurchaseItem,Valid,Frozen,InventoryUOM,ItemWarehouseInfoCollection";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(ValueProperty, out var arr))
        {
            return [];
        }

        var items = new List<SapItemValidationData>();
        foreach (var item in arr.EnumerateArray())
        {
            var parsed = ParseItemValidationData(item);
            if (parsed is not null)
            {
                items.Add(parsed);
            }
        }

        return items;
    }

    private static SapItemValidationData? ParseItemValidationData(JsonElement item)
    {
        if (!item.TryGetProperty(ItemCodeProperty, out var ic) || ic.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return new SapItemValidationData(
            ic.GetString()!,
            item.TryGetProperty("ItemName", out var iname) && iname.ValueKind == JsonValueKind.String ? iname.GetString()! : string.Empty,
            IsTyes(item, "PurchaseItem"),
            IsTyes(item, "Valid"),
            IsTyes(item, "Frozen"),
            item.TryGetProperty("InventoryUOM", out var uom) && uom.ValueKind == JsonValueKind.String ? uom.GetString() : null,
            ParseItemWarehouses(item)
        );
    }

    private static List<SapItemWarehouseData> ParseItemWarehouses(JsonElement item)
    {
        var warehouses = new List<SapItemWarehouseData>();
        if (!item.TryGetProperty("ItemWarehouseInfoCollection", out var collection) ||
            collection.ValueKind != JsonValueKind.Array)
        {
            return warehouses;
        }

        foreach (var entry in collection.EnumerateArray())
        {
            if (!entry.TryGetProperty(WarehouseCodeProperty, out var wc) || wc.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            // SL can return null for numeric fields; TryGetDecimal throws on non-Number kinds
            warehouses.Add(new SapItemWarehouseData(
                wc.GetString()!,
                GetDecimalOrZero(entry, "InStock"),
                GetDecimalOrZero(entry, "Committed"),
                IsTyes(entry, "Locked")
            ));
        }

        return warehouses;
    }

    private Task<string> ExecuteWithRetryOn401Async(HttpMethod method, string resource, string? payload, CancellationToken ct)
        => ExecuteWithRetryOn401Async(method, resource, payload, ct, companyDb: null, password: null);

    private async Task<string> ExecuteWithRetryOn401Async(
        HttpMethod method, string resource, string? payload, CancellationToken ct, string? companyDb, string? password)
    {
        await _throttle.WaitAsync(ct);
        try
        {
            var token = companyDb is null
                ? await _sessionManager.GetSessionTokenAsync(ct)
                : await _sessionManager.GetSessionTokenAsync(companyDb, password!, ct);
            try
            {
                return await _client.SendAsync(method, resource, payload, token, ct);
            }
            catch (SapException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("SAP returned 401 for {Resource}. Invalidating session and retrying once.", resource); // NOSONAR
                if (companyDb is null)
                {
                    await _sessionManager.InvalidateSessionAsync(ct);
                }
                else
                {
                    await _sessionManager.InvalidateSessionAsync(companyDb, ct);
                }
                var newToken = companyDb is null
                    ? await _sessionManager.GetSessionTokenAsync(ct)
                    : await _sessionManager.GetSessionTokenAsync(companyDb, password!, ct);
                return await _client.SendAsync(method, resource, payload, newToken, ct);
            }
        }
        finally
        {
            _throttle.Release();
        }
    }


    public async Task<IReadOnlyList<WarehouseResponse>> GetWarehousesAsync(CancellationToken ct = default)
    {
        var warehouses = new List<WarehouseResponse>();
        string? resource = "Warehouses?$select=WarehouseCode,WarehouseName";

        // SL pages results (default 20 per page); follow odata.nextLink until exhausted
        const int maxPages = 100;
        for (var page = 0; resource is not null && page < maxPages; page++)
        {
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty(ValueProperty, out var arr))
            {
                warehouses.AddRange(ParseWarehouses(arr));
            }

            resource = GetNextLink(root);
        }

        return warehouses;
    }

    public async Task<IReadOnlyList<WarehouseResponse>> GetWarehousesByCodesAsync(
        IReadOnlyList<string> warehouseCodes, CancellationToken ct = default)
    {
        if (warehouseCodes.Count == 0)
        {
            return [];
        }

        var filter = string.Join(" or ", warehouseCodes.Select(c => $"WarehouseCode eq '{c.Replace("'", "''")}'"));
        var resource = $"Warehouses?$filter={filter}&$select=WarehouseCode,WarehouseName";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(ValueProperty, out var arr)
            ? ParseWarehouses(arr)
            : [];
    }

    private static List<WarehouseResponse> ParseWarehouses(JsonElement arr)
    {
        var warehouses = new List<WarehouseResponse>();
        foreach (var entry in arr.EnumerateArray())
        {
            if (!entry.TryGetProperty(WarehouseCodeProperty, out var wc) || wc.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            warehouses.Add(new WarehouseResponse(
                wc.GetString()!,
                entry.TryGetProperty("WarehouseName", out var wn) && wn.ValueKind == JsonValueKind.String
                    ? wn.GetString()!
                    : string.Empty
            ));
        }
        return warehouses;
    }

    private static decimal GetDecimalOrZero(JsonElement el, string field) =>
        el.TryGetProperty(field, out var p) &&
        p.ValueKind == JsonValueKind.Number &&
        p.TryGetDecimal(out var v) ? v : 0m;

    // SAP Service Layer encodes boolean flags as "tYES"/"tNO" strings.
    // This helper normalises both that encoding and actual JSON booleans.
    private static bool IsTyes(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var p))
        {
            return false;
        }
        if (p.ValueKind == JsonValueKind.True)
        {
            return true;
        }
        if (p.ValueKind == JsonValueKind.False)
        {
            return false;
        }
        return p.ValueKind == JsonValueKind.String &&
               string.Equals(p.GetString(), "tYES", StringComparison.OrdinalIgnoreCase);
    }

    private static DocumentCreatedResult ParseDocumentResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("DocEntry", out var docEntryEl) || !docEntryEl.TryGetInt32(out var docEntry))
        {
            throw new SapResponseException("SAP no devolvió el campo 'DocEntry' en la respuesta de creación del documento");
        }

        if (!root.TryGetProperty("DocNum", out var docNumEl) || !docNumEl.TryGetInt32(out var docNum))
        {
            throw new SapResponseException("SAP no devolvió el campo 'DocNum' en la respuesta de creación del documento");
        }

        return new DocumentCreatedResult(docEntry, docNum);
    }
}
