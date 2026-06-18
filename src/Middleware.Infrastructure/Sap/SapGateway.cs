using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Polly.Timeout;

namespace Middleware.Infrastructure.Sap;

internal sealed class SapGateway : ISapGateway
{
    private readonly ISapSessionManager _sessionManager;
    private readonly ISapServiceLayerClient _client;
    private readonly SapThrottle _throttle;
    private readonly ILogger<SapGateway> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SapGateway(
        ISapSessionManager sessionManager,
        ISapServiceLayerClient client,
        SapThrottle throttle,
        ILogger<SapGateway> logger)
    {
        _sessionManager = sessionManager;
        _client = client;
        _throttle = throttle;
        _logger = logger;
    }

    public async Task<DocumentCreatedResult> CreateStockTransferAsync(CreateStockTransferRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            DocDate = request.DocDate.ToString("yyyy-MM-dd"),
            request.Comments,
            FromWarehouse = request.FromWarehouse,
            ToWarehouse = request.ToWarehouse,
            StockTransferLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                FromWarehouseCode = request.FromWarehouse,
                WarehouseCode = request.ToWarehouse,
                l.ProjectCode
            })
        }, JsonOptions);

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, "StockTransfers", payload, ct);
        return ParseDocumentResult(json);
    }

    public async Task<DocumentCreatedResult> CreateStockTransferWithBinsAsync(CreateStockTransferWithBinsRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            DocDate = request.DocDate.ToString("yyyy-MM-dd"),
            request.Comments,
            FromWarehouse = request.FromWarehouse,
            ToWarehouse = request.ToWarehouse,
            StockTransferLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                FromWarehouseCode = request.FromWarehouse,
                WarehouseCode = request.ToWarehouse,
                BinAllocations = new[]
                {
                    new { BinAbsEntry = l.FromBinCode, Quantity = l.Quantity, AllocationMethod = "bamByBinCode", BinActionType = "batFromWarehouse" },
                    new { BinAbsEntry = l.ToBinCode, Quantity = l.Quantity, AllocationMethod = "bamByBinCode", BinActionType = "batToWarehouse" }
                }
            })
        }, JsonOptions);

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, "StockTransfers", payload, ct);
        return ParseDocumentResult(json);
    }

    public async Task<DocumentCreatedResult> CreateStockTransferRequestAsync(CreateStockTransferRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            DocDate = request.DocDate.ToString("yyyy-MM-dd"),
            request.Comments,
            FromWarehouse = request.FromWarehouse,
            ToWarehouse = request.ToWarehouse,
            StockTransferLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                FromWarehouseCode = request.FromWarehouse,
                WarehouseCode = request.ToWarehouse,
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
                WarehouseCode = l.WarehouseCode
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
                WarehouseCode = l.WarehouseCode
            })
        }, JsonOptions);

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, "PurchaseDeliveryNotes", payload, ct);
        return ParseDocumentResult(json);
    }

    public async Task<DocumentCreatedResult> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            CardCode  = request.CardCode,
            DocDate   = request.DocDate.ToString("yyyy-MM-dd"),
            DocDueDate= request.DocDueDate.ToString("yyyy-MM-dd"),
            TaxDate   = request.TaxDate.ToString("yyyy-MM-dd"),
            request.Comments,
            U_ORIGEN  = request.U_ORIGEN,
            U_USUARIO = request.U_USUARIO,
            U_FECHA   = request.U_FECHA?.ToString("yyyy-MM-dd"),
            U_FE_ETG  = request.U_FE_ETG?.ToString("yyyy-MM-dd"),
            U_LOTE    = request.U_LOTE,
            DocumentLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                l.UnitPrice,
                WarehouseCode = l.WarehouseCode
            })
        }, JsonOptions);

        try
        {
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, "PurchaseOrders", payload, ct);
            return ParseDocumentResult(json);
        }
        catch (TimeoutRejectedException) when (!string.IsNullOrEmpty(request.Comments))
        {
            _logger.LogWarning(
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
            if (!doc.RootElement.TryGetProperty("value", out var arr))
                return null;

            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("DocEntry", out var de) && de.TryGetInt32(out var docEntry) &&
                    item.TryGetProperty("DocNum",  out var dn) && dn.TryGetInt32(out var docNum))
                    return new DocumentCreatedResult(docEntry, docNum);
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

    public async Task<DocumentCreatedResult> CreateLiquidacionAsync(CreateLiquidacionRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            DocDate = request.DocDate.ToString("yyyy-MM-dd"),
            request.Comments,
            DocumentLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                WarehouseCode = l.WarehouseCode
            })
        }, JsonOptions);

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, "InventoryGenExits", payload, ct);
        return ParseDocumentResult(json);
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
            _logger.LogWarning("SAP PurchaseOrder DocEntry={DocEntry} not found (SAP 404)", docEntry);
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int RequiredInt(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.TryGetInt32(out var v)) { return v; }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de PurchaseOrder DocEntry={docEntry}");
        }
        string RequiredString(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String) { return p.GetString()!; }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de PurchaseOrder DocEntry={docEntry}");
        }
        decimal RequiredDecimal(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.TryGetDecimal(out var v)) { return v; }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de PurchaseOrder DocEntry={docEntry}");
        }

        var lines = root.TryGetProperty("DocumentLines", out var linesEl)
            ? linesEl.EnumerateArray()
                .Select((l, i) => new PurchaseOrderLineResponse(
                    RequiredInt(l, "LineNum", $"DocumentLines[{i}]"),
                    RequiredString(l, "ItemCode", $"DocumentLines[{i}]"),
                    RequiredDecimal(l, "Quantity", $"DocumentLines[{i}]"),
                    RequiredDecimal(l, "OpenQuantity", $"DocumentLines[{i}]"),
                    RequiredDecimal(l, "UnitPrice", $"DocumentLines[{i}]"),
                    RequiredString(l, "WarehouseCode", $"DocumentLines[{i}]")
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
        var items = node?["value"]?.AsArray()
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
            _logger.LogWarning("SAP PurchaseOrder DocEntry={DocEntry} not found (SAP 404)", docEntry);
            throw;
        }
    }

    public async Task<ItemResponse?> GetItemAsync(string itemCode, CancellationToken ct = default)
    {
        string json;
        try
        {
            var encoded = Uri.EscapeDataString(itemCode);
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items('{encoded}')?$select=ItemCode,ItemName,InventoryUoMEntry,QuantityOnStock,PurchaseItem", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP Item '{ItemCode}' not found", itemCode);
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new ItemResponse(
            root.TryGetProperty("ItemCode",         out var ic)    && ic.ValueKind    == JsonValueKind.String ? ic.GetString()!    : itemCode,
            root.TryGetProperty("ItemName",         out var iname) && iname.ValueKind == JsonValueKind.String ? iname.GetString()! : string.Empty,
            root.TryGetProperty("InventoryUoMEntry",out var uom)   && uom.ValueKind   == JsonValueKind.String ? uom.GetString()    : null,
            root.TryGetProperty("QuantityOnStock",   out var oh)    && oh.TryGetDecimal(out var ohVal)         ? ohVal              : 0m,
            IsTyes(root, "PurchaseItem")
        );
    }

    public async Task<ItemStockResponse?> GetItemStockAsync(string itemCode, string warehouseCode, CancellationToken ct = default)
    {
        string json;
        try
        {
            var encoded = Uri.EscapeDataString(itemCode);
            // Fetch the item with its per-warehouse collection and filter in-memory;
            // SAP SL does not support $filter inside nested collections via OData.
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items('{encoded}')?$select=ItemCode,ItemWarehouseInfoCollection", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP Item '{ItemCode}' not found when querying stock", itemCode);
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("ItemWarehouseInfoCollection", out var collection))
            return null;

        foreach (var entry in collection.EnumerateArray())
        {
            if (!entry.TryGetProperty("WarehouseCode", out var wc) || wc.GetString() != warehouseCode)
                continue;

            var inStock = entry.TryGetProperty("InStock", out var s) && s.TryGetDecimal(out var sv) ? sv : 0m;
            return new ItemStockResponse(itemCode, warehouseCode, inStock);
        }

        // Warehouse found in SAP catalog but no entry for this warehouse → stock 0
        return new ItemStockResponse(itemCode, warehouseCode, 0m);
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
            _logger.LogWarning("SAP BusinessPartner '{CardCode}' not found", cardCode);
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

    public async Task<InventoryTransferRequestsListResponse> GetInventoryTransferRequestsByCommentAsync(
        string commentsContain, CancellationToken ct = default)
    {
        var escaped = commentsContain.Replace("'", "''");
        var resource = $"InventoryTransferRequests?$filter=contains(Comments,'{escaped}')&$select=DocEntry,DocNum,Comments,FromWarehouse,ToWarehouse,DocDate,DocumentStatus,StockTransferLines";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("value", out var arr))
            return new InventoryTransferRequestsListResponse([]);

        var items = arr.EnumerateArray().Select(item =>
        {
            int GetInt(string f) => item.TryGetProperty(f, out var p) && p.TryGetInt32(out var v) ? v : 0;
            string GetStr(string f) => item.TryGetProperty(f, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : string.Empty;
            string? GetStrOpt(string f) => item.TryGetProperty(f, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

            var lines = item.TryGetProperty("StockTransferLines", out var linesEl)
                ? linesEl.EnumerateArray().Select((l, i) => new InventoryTransferRequestLineResponse(
                    l.TryGetProperty("LineNum", out var ln) && ln.TryGetInt32(out var lv) ? lv : i,
                    l.TryGetProperty("ItemCode", out var li) && li.ValueKind == JsonValueKind.String ? li.GetString()! : string.Empty,
                    l.TryGetProperty("Quantity", out var lq) && lq.TryGetDecimal(out var lqv) ? lqv : 0m,
                    l.TryGetProperty("WarehouseCode", out var lw) && lw.ValueKind == JsonValueKind.String ? lw.GetString() : null
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

    private async Task<string> ExecuteWithRetryOn401Async(HttpMethod method, string resource, string? payload, CancellationToken ct)
    {
        await _throttle.WaitAsync(ct);
        try
        {
            var token = await _sessionManager.GetSessionTokenAsync(ct);
            try
            {
                return await _client.SendAsync(method, resource, payload, token, ct);
            }
            catch (SapException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("SAP returned 401 for {Resource}. Invalidating session and retrying once.", resource);
                await _sessionManager.InvalidateSessionAsync(ct);
                var newToken = await _sessionManager.GetSessionTokenAsync(ct);
                return await _client.SendAsync(method, resource, payload, newToken, ct);
            }
        }
        finally
        {
            _throttle.Release();
        }
    }


    // SAP Service Layer encodes boolean flags as "tYES"/"tNO" strings.
    // This helper normalises both that encoding and actual JSON booleans.
    private static bool IsTyes(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var p)) return false;
        if (p.ValueKind == JsonValueKind.True)  return true;
        if (p.ValueKind == JsonValueKind.False) return false;
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
