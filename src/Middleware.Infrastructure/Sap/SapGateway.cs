using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

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
                WarehouseCode = l.WarehouseCode,
                AccountCode = l.AccountCode
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
                WarehouseCode = l.WarehouseCode,
                AccountCode = l.AccountCode
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
