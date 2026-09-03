using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Infrastructure.Configuration;
using Middleware.Infrastructure.WebPos;
using Polly.Timeout;

namespace Middleware.Infrastructure.Sap;

internal sealed class SapGateway : ISapGateway
{
    private readonly ISapSessionManager _sessionManager;
    private readonly ISapServiceLayerClient _client;
    private readonly SapThrottle _throttle;
    private readonly ItemGroupsCache _itemGroupsCache;
    private readonly WarehousePosInfoCache _warehousePosInfoCache;
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
        WarehousePosInfoCache warehousePosInfoCache,
        IOptions<SapSettings> settings,
        ILogger<SapGateway> logger)
    {
        _sessionManager = sessionManager;
        _client = client;
        _throttle = throttle;
        _itemGroupsCache = itemGroupsCache;
        _warehousePosInfoCache = warehousePosInfoCache;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<DocumentCreatedResult> CreateStockTransferBasedOnDocumentAsync(CreateStockTransferBasedOnDocumentRequest request, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();

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
            HttpMethod.Post, "StockTransfers", payload, _settings.TestCompanyDb, _settings.TestPassword, ct);
        return ParseDocumentResult(json);
    }

    public async Task<DocumentCreatedResult> CreateStockTransferForCompanyAsync(CreateStockTransferBasedOnDocumentRequest request, CancellationToken ct = default)
    {
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

        // Sin companyDb/password: usa Sap:CompanyDb / Sap:Password (la compañía real), NO
        // Sap:TestCompanyDb — a diferencia de CreateStockTransferBasedOnDocumentAsync de
        // arriba (ese es del sandbox de WebPOS).
        var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, "StockTransfers", payload, ct);
        return ParseDocumentResult(json);
    }

    public async Task<DocumentCreatedResult> CreateStockTransferDirectAsync(CreateStockTransferDirectRequest request, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();

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
                l.ItemCode,
                l.Quantity,
                l.WarehouseCode
            })
        }, JsonOptions);

        var json = await ExecuteWithRetryOn401Async(
            HttpMethod.Post, "StockTransfers", payload, _settings.TestCompanyDb, _settings.TestPassword, ct);
        return ParseDocumentResult(json);
    }

    public async Task<DocumentCreatedResult> CreateStockTransferWithBinsAsync(CreateStockTransferWithBinsRequest request, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();

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
                l.WarehouseCode,
                // Null si esa línea no trae bins (bodega sin ubicaciones en ese lado) — con
                // DefaultIgnoreCondition.WhenWritingNull el campo se omite del payload a SAP.
                StockTransferLinesBinAllocations = l.StockTransferLinesBinAllocations?.Select(b => new
                {
                    b.BinAbsEntry,
                    b.Quantity,
                    AllocationMethod = "bamByBinCode",
                    // 1 = Entrada (bin destino) → batToWarehouse; 2 = Salida (bin origen) → batFromWarehouse.
                    BinActionType = b.BinActionType == 1 ? "batToWarehouse" : "batFromWarehouse" // NOSONAR
                })
            })
        }, JsonOptions);

        var json = await ExecuteWithRetryOn401Async(
            HttpMethod.Post, "StockTransfers", payload, _settings.TestCompanyDb, _settings.TestPassword, ct);
        return ParseDocumentResult(json);
    }

    private void EnsureTestCredentialsConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.TestCompanyDb))
        {
            throw new InvalidOperationException("Sap:TestCompanyDb no está configurado.");
        }

        if (string.IsNullOrWhiteSpace(_settings.TestPassword))
        {
            throw new InvalidOperationException("Sap:TestPassword no está configurado.");
        }
    }

    public async Task<DocumentCreatedResult> CreateStockTransferRequestAsync(CreateStockTransferRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            DocDate = DateTime.Now.ToString("yyyy-MM-dd"), // NOSONAR
            request.Comments,
            FromWarehouse = request.FromWarehouse,
            ToWarehouse = request.ToWarehouse,
            request.U_ORIGEN,
            Series = request.Series,
            StockTransferLines = request.StockTransferLines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                l.WarehouseCode
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

    // Object type de SAP B1 para Purchase Orders — lo que marca un GoodsReceipt como
    // "Copy From" de una PO puntual (reduce su pendiente y la deja cerrarse sola) en vez de
    // un documento suelto sin relación con ninguna orden de compra.
    private const int SapPurchaseOrdersObjectType = 22;

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
                WarehouseCode = l.WarehouseCode, // NOSONAR
                BaseType = l.BaseEntry.HasValue ? SapPurchaseOrdersObjectType : (int?)null,
                l.BaseEntry,
                l.BaseLine
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
                    // El nombre real en SAP Service Layer es "RemainingOpenQuantity" —
                    // "OpenQuantity" no existe en este esquema y hacía fallar cualquier
                    // PurchaseOrder con líneas reales (ver hallazgo de api-cedi, mismo bug).
                    RequiredDecimal(l, "RemainingOpenQuantity", $"DocumentLines[{i}]"),
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

    public async Task UpdatePurchaseOrderStatusAsync(int docEntry, string status, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            U_STATUS = status // NOSONAR
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

    public Task<ItemStockResponse?> GetItemStockAsync(string itemCode, string warehouseCode, CancellationToken ct = default)
        => GetItemStockInternalAsync(itemCode, warehouseCode, companyDb: null, password: null, ct);

    /// <summary>Same data as <see cref="GetItemStockAsync"/> but against Sap:TestCompanyDb — see interface doc.</summary>
    public Task<ItemStockResponse?> GetItemStockForTestCompanyAsync(string itemCode, string warehouseCode, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();
        return GetItemStockInternalAsync(itemCode, warehouseCode, _settings.TestCompanyDb, _settings.TestPassword, ct);
    }

    private async Task<ItemStockResponse?> GetItemStockInternalAsync(
        string itemCode, string warehouseCode, string? companyDb, string? password, CancellationToken ct)
    {
        string json;
        try
        {
            var encoded = Uri.EscapeDataString(itemCode);
            // Fetch the item with its per-warehouse collection and filter in-memory; // NOSONAR
            // SAP SL does not support $filter inside nested collections via OData.
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items('{encoded}')?$select=ItemCode,InventoryUOM,ItemWarehouseInfoCollection", null, companyDb, password, ct);
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

    public async Task<IReadOnlyList<ItemSearchResult>> SearchItemsAsync(string query, int top, CancellationToken ct = default)
    {
        var escaped = query.Replace("'", "''");
        var resource = $"Items?$filter=contains(ItemCode,'{escaped}') or contains(BarCode,'{escaped}')" +
                       $"&$select=ItemCode,ItemName,BarCode,InventoryUOM&$top={top}";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty(ValueProperty, out var arr))
        {
            return [];
        }

        var results = new List<ItemSearchResult>();
        foreach (var item in arr.EnumerateArray())
        {
            if (!item.TryGetProperty(ItemCodeProperty, out var icEl) || icEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var itemName = item.TryGetProperty("ItemName", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString()! : string.Empty;
            var barCode = item.TryGetProperty("BarCode", out var bcEl) && bcEl.ValueKind == JsonValueKind.String
                ? bcEl.GetString() : null;
            var uom = item.TryGetProperty("InventoryUOM", out var uomEl) && uomEl.ValueKind == JsonValueKind.String
                ? uomEl.GetString() : null;

            results.Add(new ItemSearchResult(icEl.GetString()!, itemName, barCode, uom));
        }

        return results;
    }

    public async Task<IReadOnlyList<ItemStockResponse>?> GetItemStockAllWarehousesAsync(string itemCode, CancellationToken ct = default)
    {
        string json;
        try
        {
            var encoded = Uri.EscapeDataString(itemCode);
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items('{encoded}')?$select=ItemCode,InventoryUOM,ItemWarehouseInfoCollection", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP Item '{ItemCode}' not found when querying stock (all warehouses)", itemCode); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("ItemWarehouseInfoCollection", out var collection))
        {
            return [];
        }

        var uomCode = root.TryGetProperty("InventoryUOM", out var uom) && uom.ValueKind == JsonValueKind.String
            ? uom.GetString() : null;

        var result = new List<ItemStockResponse>();
        foreach (var entry in collection.EnumerateArray())
        {
            if (!entry.TryGetProperty(WarehouseCodeProperty, out var wc) || wc.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var inStock = GetDecimalOrZero(entry, "InStock");
            var committed = GetDecimalOrZero(entry, "Committed");
            if (inStock == 0 && committed == 0)
            {
                continue; // "mostrar por ubicación" solo tiene sentido donde hay algo
            }

            result.Add(new ItemStockResponse(itemCode, wc.GetString()!, inStock, committed, uomCode, IsTyes(entry, "Locked")));
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, WarehousePosInfo>> GetWarehousesPosInfoForTestCompanyAsync(
        IReadOnlyList<string> warehouseCodes, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();

        if (warehouseCodes.Count == 0)
        {
            return new Dictionary<string, WarehousePosInfo>();
        }

        var fullMap = await _warehousePosInfoCache.GetOrLoadAsync(
            _settings.TestCompanyDb!,
            loaderCt => LoadWarehousesPosInfoAsync(_settings.TestCompanyDb, _settings.TestPassword, loaderCt), ct);

        return warehouseCodes
            .Where(fullMap.ContainsKey)
            .ToDictionary(code => code, code => fullMap[code]);
    }

    /// <summary>Same POS flags but from the default company (Sap:CompanyDb) — see interface doc.</summary>
    public async Task<IReadOnlyDictionary<string, WarehousePosInfo>> GetWarehousesPosInfoAsync(
        IReadOnlyList<string> warehouseCodes, CancellationToken ct = default)
    {
        if (warehouseCodes.Count == 0)
        {
            return new Dictionary<string, WarehousePosInfo>();
        }

        // Cache keyed by company DB, so the default-company map never mixes with the test-company one.
        var fullMap = await _warehousePosInfoCache.GetOrLoadAsync(
            _settings.CompanyDb,
            loaderCt => LoadWarehousesPosInfoAsync(companyDb: null, password: null, loaderCt), ct);

        return warehouseCodes
            .Where(fullMap.ContainsKey)
            .ToDictionary(code => code, code => fullMap[code]);
    }

    private async Task<IReadOnlyDictionary<string, WarehousePosInfo>> LoadWarehousesPosInfoAsync(
        string? companyDb, string? password, CancellationToken ct)
    {
        var result = new Dictionary<string, WarehousePosInfo>();
        string? resource = "Warehouses?$select=WarehouseCode,U_WEBPOS,U_NEGOCIO";

        const int maxPages = 100;
        for (var page = 0; resource is not null && page < maxPages; page++)
        {
            var json = await ExecuteWithRetryOn401Async(
                HttpMethod.Get, resource, null, companyDb, password, ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty(ValueProperty, out var arr))
            {
                foreach (var entry in arr.EnumerateArray())
                {
                    if (!entry.TryGetProperty(WarehouseCodeProperty, out var wc) || wc.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var code = wc.GetString()!;

                    // U_WEBPOS is a plain alphanumeric UDF ('S'/other), not a SAP boolean field —
                    // it must be compared as a string, unlike IsTyes()'s tYES/tNO encoding.
                    var isPosEnabled = entry.TryGetProperty("U_WEBPOS", out var wp) &&
                        wp.ValueKind == JsonValueKind.String &&
                        string.Equals(wp.GetString(), "S", StringComparison.OrdinalIgnoreCase);

                    var codBusiness = entry.TryGetProperty("U_NEGOCIO", out var neg) &&
                        neg.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(neg.GetString())
                            ? neg.GetString()
                            : null;

                    result[code] = new WarehousePosInfo(code, isPosEnabled, codBusiness);
                }
            }

            resource = GetNextLink(root);
        }

        return result;
    }

    public async Task<StockTransferDocumentResponse?> GetStockTransferForTestCompanyAsync(int docEntry, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();

        string json;
        try
        {
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"StockTransfers({docEntry})?$select=DocEntry,DocNum,FromWarehouse,ToWarehouse,StockTransferLines",
                null, _settings.TestCompanyDb, _settings.TestPassword, ct);
        }
        catch (SapException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP StockTransfer DocEntry={DocEntry} not found (test company)", docEntry); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int GetInt(string f) => root.TryGetProperty(f, out var p) && p.TryGetInt32(out var v) ? v : 0;
        string GetStr(string f) => root.TryGetProperty(f, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : string.Empty;

        var fromWarehouse = GetStr("FromWarehouse");
        var toWarehouse = GetStr("ToWarehouse");

        var lines = new List<StockTransferDocumentLine>();
        if (root.TryGetProperty("StockTransferLines", out var linesEl) && linesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in linesEl.EnumerateArray())
            {
                if (!line.TryGetProperty(ItemCodeProperty, out var ic) || ic.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var quantity = line.TryGetProperty("Quantity", out var q) && q.TryGetDecimal(out var qv) ? qv : 0m;
                var warehouseCode = line.TryGetProperty(WarehouseCodeProperty, out var w) && w.ValueKind == JsonValueKind.String
                    ? w.GetString()!
                    : toWarehouse;

                lines.Add(new StockTransferDocumentLine(ic.GetString()!, quantity, warehouseCode));
            }
        }

        return new StockTransferDocumentResponse(GetInt("DocEntry"), GetInt("DocNum"), fromWarehouse, toWarehouse, lines);
    }

    public async Task<GoodsReceiptDocumentResponse?> GetGoodsReceiptAsync(int docEntry, CancellationToken ct = default)
    {
        string json;
        try
        {
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"PurchaseDeliveryNotes({docEntry})?$select=DocEntry,DocNum,DocumentLines",
                null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP PurchaseDeliveryNote DocEntry={DocEntry} not found", docEntry); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int GetInt(string f) => root.TryGetProperty(f, out var p) && p.TryGetInt32(out var v) ? v : 0;

        var lines = new List<GoodsReceiptDocumentLine>();
        if (root.TryGetProperty("DocumentLines", out var linesEl) && linesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in linesEl.EnumerateArray())
            {
                if (!line.TryGetProperty(ItemCodeProperty, out var ic) || ic.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!line.TryGetProperty(WarehouseCodeProperty, out var w) || w.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var quantity = line.TryGetProperty("Quantity", out var q) && q.TryGetDecimal(out var qv) ? qv : 0m;
                lines.Add(new GoodsReceiptDocumentLine(ic.GetString()!, quantity, w.GetString()!));
            }
        }

        return new GoodsReceiptDocumentResponse(GetInt("DocEntry"), GetInt("DocNum"), lines);
    }

    /// <summary>
    /// Master-data fields WEBPOS needs to auto-create an item/store pair the first time it sees
    /// it ("El producto no existe."). Same source fields ProductosMabel already uses to create
    /// articles in HITELL: price list 1, U_IXX_MARCA, the tax code's U_X4R_INTERKEY, and every
    /// barcode on the item. Best-effort — a field SAP can't resolve comes back null rather than
    /// failing the whole lookup, since the auto-create retry is itself best-effort.
    /// </summary>
    public Task<ItemPosEnrichment?> GetItemPosEnrichmentForTestCompanyAsync(string itemCode, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();
        return GetItemPosEnrichmentInternalAsync(itemCode, _settings.TestCompanyDb, _settings.TestPassword, ct);
    }

    /// <summary>Same enrichment but from the default company (Sap:CompanyDb) — see interface doc.</summary>
    public Task<ItemPosEnrichment?> GetItemPosEnrichmentAsync(string itemCode, CancellationToken ct = default)
        => GetItemPosEnrichmentInternalAsync(itemCode, companyDb: null, password: null, ct);

    private async Task<ItemPosEnrichment?> GetItemPosEnrichmentInternalAsync(
        string itemCode, string? companyDb, string? password, CancellationToken ct)
    {
        string json;
        try
        {
            var encoded = Uri.EscapeDataString(itemCode);
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items('{encoded}')?$select=ItemCode,ArTaxCode,U_IXX_MARCA,ItemPrices,ItemBarCodeCollection",
                null, companyDb, password, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP Item '{ItemCode}' not found (company={CompanyDb}) when enriching for WEBPOS auto-create", itemCode, companyDb ?? _settings.CompanyDb); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? price = null;
        if (root.TryGetProperty("ItemPrices", out var prices) && prices.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in prices.EnumerateArray())
            {
                var isPriceList1 = p.TryGetProperty("PriceList", out var pl) &&
                    (pl.ValueKind == JsonValueKind.String ? pl.GetString() == "1" : pl.ValueKind == JsonValueKind.Number && pl.TryGetInt32(out var plNum) && plNum == 1);

                if (isPriceList1 && p.TryGetProperty("Price", out var priceEl) && priceEl.TryGetDecimal(out var priceVal))
                {
                    price = Math.Round(priceVal, 3).ToString(CultureInfo.InvariantCulture);
                    break;
                }
            }
        }

        var brand = root.TryGetProperty("U_IXX_MARCA", out var b) && b.ValueKind == JsonValueKind.String
            ? b.GetString()
            : null;

        string? codeTax = null;
        if (root.TryGetProperty("ArTaxCode", out var taxCodeEl) &&
            taxCodeEl.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(taxCodeEl.GetString()))
        {
            codeTax = await TryGetTaxInterkeyAsync(taxCodeEl.GetString()!, companyDb, password, ct);
        }

        var barcodes = new List<string>();
        if (root.TryGetProperty("ItemBarCodeCollection", out var bc) && bc.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in bc.EnumerateArray())
            {
                if (entry.TryGetProperty("Barcode", out var bcEl) &&
                    bcEl.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(bcEl.GetString()))
                {
                    barcodes.Add(bcEl.GetString()!);
                }
            }
        }

        return new ItemPosEnrichment(price, brand, codeTax, barcodes);
    }

    private async Task<string?> TryGetTaxInterkeyAsync(string taxCode, string? companyDb, string? password, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(taxCode);
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"SalesTaxCodes('{encoded}')?$select=Code,U_X4R_INTERKEY",
                null, companyDb, password, ct);

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("U_X4R_INTERKEY", out var interkey) && interkey.ValueKind == JsonValueKind.String
                ? interkey.GetString()
                : null;
        }
        catch (SapException ex)
        {
            // Enrichment is best-effort — a missing/invalid tax code must not block the retry.
            _logger.LogWarning(ex, "Could not resolve U_X4R_INTERKEY for ArTaxCode='{TaxCode}' while enriching for WEBPOS auto-create", taxCode); // NOSONAR
            return null;
        }
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

    public async Task<InventoryTransferRequestsRawListResponse> GetInventoryTransferRequestsAsync(
        GetInventoryTransferRequestsRequest request, CancellationToken ct = default)
    {
        var resource = $"InventoryTransferRequests?$top={request.Top}&$skip={request.Skip}&$orderby=DocEntry desc";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        var items = node?[ValueProperty]?.AsArray()
            .OfType<System.Text.Json.Nodes.JsonObject>()
            .ToList()
            ?? [];

        return new InventoryTransferRequestsRawListResponse(items, items.Count);
    }

    public async Task<InventoryTransferRequestResponse?> GetInventoryTransferRequestAsync(int docEntry, CancellationToken ct = default)
    {
        string json;
        try
        {
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get, $"InventoryTransferRequests({docEntry})", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP InventoryTransferRequest DocEntry={DocEntry} not found (SAP 404)", docEntry); // NOSONAR
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
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de InventoryTransferRequest DocEntry={docEntry}");
        }
        string RequiredString(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String)
            {
                return p.GetString()!;
            }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de InventoryTransferRequest DocEntry={docEntry}");
        }
        decimal RequiredDecimal(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.TryGetDecimal(out var v))
            {
                return v;
            }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de InventoryTransferRequest DocEntry={docEntry}");
        }
        string? OptString(JsonElement el, string field)
            => el.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        var lines = root.TryGetProperty("StockTransferLines", out var linesEl)
            ? linesEl.EnumerateArray()
                .Select((l, i) => new InventoryTransferRequestLineResponse(
                    RequiredInt(l, "LineNum", $"StockTransferLines[{i}]"),
                    RequiredString(l, ItemCodeProperty, $"StockTransferLines[{i}]"),
                    RequiredDecimal(l, "Quantity", $"StockTransferLines[{i}]"),
                    RequiredString(l, WarehouseCodeProperty, $"StockTransferLines[{i}]"),
                    // Confirmado en vivo contra TEST_MABEL: el campo real es "RemainingOpenQuantity"
                    // (mismo nombre que en PurchaseOrders) — no "OpenQuantity".
                    RequiredDecimal(l, "RemainingOpenQuantity", $"StockTransferLines[{i}]"),
                    OptString(l, "ItemDescription"),
                    OptString(l, "FromWarehouseCode")
                )).ToList()
            : (IReadOnlyList<InventoryTransferRequestLineResponse>)[];

        return new InventoryTransferRequestResponse(
            RequiredInt(root, "DocEntry", "root"),
            RequiredInt(root, "DocNum", "root"),
            OptString(root, "Comments"),
            RequiredString(root, "FromWarehouse", "root"),
            RequiredString(root, "ToWarehouse", "root"),
            RequiredString(root, "DocDate", "root"),
            RequiredString(root, "DocumentStatus", "root"),
            lines
        );
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
        => ExecuteWithRetryOn401Async(method, resource, payload, companyDb: null, password: null, ct);

    private async Task<string> ExecuteWithRetryOn401Async(
        HttpMethod method, string resource, string? payload, string? companyDb, string? password, CancellationToken ct)
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
