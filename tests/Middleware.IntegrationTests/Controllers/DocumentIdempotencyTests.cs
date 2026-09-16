using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Middleware.Application.Dtos.Requests;
using Middleware.Domain.Enums;
using Middleware.Infrastructure.Sap;
using Middleware.IntegrationTests.Infrastructure;
using Polly.Timeout;

namespace Middleware.IntegrationTests.Controllers;

/// <summary>
/// Idempotency by U_sap_id on POST /goods-receipts (PurchaseDeliveryNotes) and POST /deposits
/// (InventoryGenEntries). Both are opt-in: without a key the request reaches SAP exactly as before
/// (no lookup, no U_sap_id in the payload), because the UDF may not exist yet on those tables.
/// </summary>
public sealed class DocumentIdempotencyTests : IAsyncLifetime
{
    private readonly ScriptedSapFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static bool IsLookup(ScriptedSapServiceLayerClient.Call c, string resource, string key)
        => c.Method == HttpMethod.Get && c.Url.StartsWith($"{resource}?$filter=U_sap_id eq '{key}'", StringComparison.Ordinal);

    private static bool IsCreate(ScriptedSapServiceLayerClient.Call c, string resource)
        => c.Method == HttpMethod.Post && c.Url == resource;

    private const string NotFound = """{"value":[]}""";

    private static CreateGoodsReceiptRequest GoodsReceipt(string? key) => new(
        VendorCode: "P001",
        DocDate: new DateOnly(2026, 9, 14),
        Comments: "Recepción ODC 9001",
        Lines: [new GoodsReceiptLineDto("ITEM-001", 100m, 12.5m, "WH01", BaseEntry: 9001, BaseLine: 0)],
        U_sap_id: key);

    private static CreateDepositRequest Deposit(string? key) => new(
        Type: DepositType.MT,
        Environment: CompanyEnvironment.Test,
        DocDate: new DateOnly(2026, 9, 14),
        Comments: null,
        Lines: [new DepositLineDto("ITEM-001", 20m, "WH01")],
        U_sap_id: key);

    private static CreateStockTransferRequest TransferRequest(string? key) => new(
        FromWarehouse: "CD01",
        ToWarehouse: "TRL-PSC",
        DocDate: new DateOnly(2026, 9, 14),
        Comments: "Reposición categoría 12",
        Lines: [new StockTransferLineDto("ITEM-001", 30m)],
        UOdc: null,
        Series: 12,
        U_ORIGEN: "C",
        U_sap_id: key);

    [Fact]
    public async Task TransferRequest_WithKey_LooksUpFirst_ThenCreatesWithUSapId()
    {
        const string key = "reposicion-solicitud-4410";
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, "InventoryTransferRequests", key) => NotFound,
            _ when IsCreate(call, "InventoryTransferRequests") => """{"DocEntry":845999,"DocNum":70248100}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/stock-transfers/requests", TransferRequest(key));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(845999);
        body.GetProperty("recovered").GetBoolean().Should().BeFalse();

        var calls = _factory.SapClient.Calls;
        calls.Should().HaveCount(2);
        IsLookup(calls[0], "InventoryTransferRequests", key).Should().BeTrue();
        using var payload = JsonDocument.Parse(calls[1].Body!);
        payload.RootElement.GetProperty("U_sap_id").GetString().Should().Be(key);
    }

    [Fact]
    public async Task TransferRequest_WithoutKey_MiddlewareGeneratesAGuid()
    {
        // The UDF exists on OWTQ (60 chars, confirmed 2026-09-14), so queue retries are protected even
        // before api-reposiciones starts sending its own key.
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            { Method.Method: "GET" } => NotFound,
            _ when IsCreate(call, "InventoryTransferRequests") => """{"DocEntry":846000,"DocNum":70248101}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/stock-transfers/requests", TransferRequest(key: null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var payload = JsonDocument.Parse(_factory.SapClient.Posts.Single().Body!);
        var generated = payload.RootElement.GetProperty("U_sap_id").GetString();
        Guid.TryParse(generated, out _).Should().BeTrue($"'{generated}' should be a GUID");
    }

    [Fact]
    public async Task TransferRequest_SameWarehousesAndItems_AlreadyExists_ReturnsItRecovered()
    {
        const string key = "reposicion-solicitud-4411";
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, "InventoryTransferRequests", key) =>
                """{"value":[{"DocEntry":846001,"DocNum":70248102,"FromWarehouse":"CD01","ToWarehouse":"TRL-PSC","StockTransferLines":[{"ItemCode":"ITEM-001","Quantity":30.0}]}]}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/stock-transfers/requests", TransferRequest(key));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(846001);
        body.GetProperty("recovered").GetBoolean().Should().BeTrue();
        _factory.SapClient.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task GoodsReceipt_WithKey_LooksUpFirst_ThenCreatesWithUSapId()
    {
        const string key = "cedi-recepcion-15";
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, "PurchaseDeliveryNotes", key) => NotFound,
            _ when IsCreate(call, "PurchaseDeliveryNotes") => """{"DocEntry":55,"DocNum":300}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/goods-receipts", GoodsReceipt(key));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(55);
        body.GetProperty("recovered").GetBoolean().Should().BeFalse();

        var calls = _factory.SapClient.Calls;
        calls.Should().HaveCount(2);
        IsLookup(calls[0], "PurchaseDeliveryNotes", key).Should().BeTrue();
        using var payload = JsonDocument.Parse(calls[1].Body!);
        payload.RootElement.GetProperty("U_sap_id").GetString().Should().Be(key);
        payload.RootElement.GetProperty("CardCode").GetString().Should().Be("P001");
    }

    [Fact]
    public async Task GoodsReceipt_WithoutKey_MiddlewareGeneratesAGuid()
    {
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            { Method.Method: "GET" } => NotFound,
            _ when IsCreate(call, "PurchaseDeliveryNotes") => """{"DocEntry":57,"DocNum":302}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/goods-receipts", GoodsReceipt(key: null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var payload = JsonDocument.Parse(_factory.SapClient.Posts.Single().Body!);
        Guid.TryParse(payload.RootElement.GetProperty("U_sap_id").GetString(), out _).Should().BeTrue();
    }

    [Fact]
    public async Task UdfMissingInSap_CreatesWithoutKey_AndStillReturns201()
    {
        // SAP rejects the unknown property → the gateway must fall back to the pre-idempotency payload
        // instead of failing the business operation (the UDF has to be created by SAP admins).
        _factory.SapClient.Handler = call =>
        {
            if (call.Method == HttpMethod.Get)
            {
                throw new SapException(HttpStatusCode.BadRequest,
                    """{"error":{"code":-1,"message":{"lang":"en-us","value":"Property 'U_sap_id' of 'Document' is invalid"}}}""");
            }

            if (IsCreate(call, "PurchaseDeliveryNotes") && call.Body!.Contains("U_sap_id", StringComparison.Ordinal))
            {
                throw new SapException(HttpStatusCode.BadRequest,
                    """{"error":{"code":-1,"message":{"lang":"en-us","value":"Property 'U_sap_id' of 'Document' is invalid"}}}""");
            }

            if (IsCreate(call, "PurchaseDeliveryNotes"))
            {
                return Task.FromResult("""{"DocEntry":59,"DocNum":304}""");
            }

            throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}");
        };

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/goods-receipts", GoodsReceipt("cedi-recepcion-18"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, "a missing UDF must never block creating the document");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(59);
        body.GetProperty("recovered").GetBoolean().Should().BeFalse();

        _factory.SapClient.Posts.Should().HaveCount(2, "first with the key, then without it");
        using var second = JsonDocument.Parse(_factory.SapClient.Posts[1].Body!);
        second.RootElement.TryGetProperty("U_sap_id", out _).Should().BeFalse();
        second.RootElement.GetProperty("CardCode").GetString().Should().Be("P001", "the rest of the payload is untouched");
    }

    [Fact]
    public async Task GoodsReceipt_SameVendorAndPurchaseOrder_AlreadyExists_ReturnsItRecovered()
    {
        const string key = "cedi-recepcion-16";
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, "PurchaseDeliveryNotes", key) =>
                """{"value":[{"DocEntry":56,"DocNum":301,"CardCode":"P001","DocumentLines":[{"ItemCode":"ITEM-001","Quantity":100.0,"BaseEntry":9001,"BaseLine":0}]}]}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/goods-receipts", GoodsReceipt(key));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(56);
        body.GetProperty("recovered").GetBoolean().Should().BeTrue();
        _factory.SapClient.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task GoodsReceipt_KeyUsedByAnotherVendor_Returns409()
    {
        const string key = "cedi-recepcion-17";
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, "PurchaseDeliveryNotes", key) =>
                """{"value":[{"DocEntry":58,"DocNum":303,"CardCode":"P999","DocumentLines":[{"ItemCode":"OTRO","Quantity":1.0,"BaseEntry":7777,"BaseLine":0}]}]}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/goods-receipts", GoodsReceipt(key));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _factory.SapClient.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task Deposit_WithKey_LooksUpFirst_ThenCreatesWithUSapId()
    {
        const string key = "depositos-mt-4410";
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, "InventoryGenEntries", key) => NotFound,
            _ when IsCreate(call, "InventoryGenEntries") => """{"DocEntry":70,"DocNum":205}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/deposits", Deposit(key));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(70);
        body.GetProperty("recovered").GetBoolean().Should().BeFalse();
        using var payload = JsonDocument.Parse(_factory.SapClient.Posts.Single().Body!);
        payload.RootElement.GetProperty("U_sap_id").GetString().Should().Be(key);
    }

    [Fact]
    public async Task Deposit_WithoutKey_MiddlewareGeneratesAGuid()
    {
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            { Method.Method: "GET" } => NotFound,
            _ when IsCreate(call, "InventoryGenEntries") => """{"DocEntry":71,"DocNum":206}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/deposits", Deposit(key: null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var payload = JsonDocument.Parse(_factory.SapClient.Posts.Single().Body!);
        Guid.TryParse(payload.RootElement.GetProperty("U_sap_id").GetString(), out _).Should().BeTrue();
    }

    private static CreatePurchaseOrderRequest PurchaseOrder(string? key) => new(
        CardCode: "P001",
        DocDate: new DateOnly(2026, 9, 14),
        DocDueDate: new DateOnly(2026, 9, 21),
        TaxDate: new DateOnly(2026, 9, 14),
        Comments: "ODC CEDI 24748",
        U_ORIGEN: "C",
        U_USUARIO: "cedi",
        U_FECHA: null,
        U_FE_ETG: null,
        U_LOTE: null,
        Lines: [new CreatePurchaseOrderLineDto("ITEM-001", 10m, 3.5m, "CD01")],
        U_sap_id: key);

    private static CreateLiquidacionRequest Liquidacion(string? key) => new(
        CardCode: "P001",
        Series: 100,
        Comments: "Liquidación caja chica 512",
        DocumentLines: [new LiquidacionLineDto("_SYS00000000123", "Servicio de transporte", 25.5m)],
        U_sap_id: key);

    [Fact]
    public async Task Liquidacion_WithKey_LooksUpFirst_ThenCreatesWithUSapId()
    {
        const string key = "cajachica-liquidacion-512";
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, "PurchaseInvoices", key) => NotFound,
            _ when IsCreate(call, "PurchaseInvoices") => """{"DocEntry":9001,"DocNum":9001}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/liquidaciones", Liquidacion(key));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(9001);
        body.GetProperty("recovered").GetBoolean().Should().BeFalse();
        using var payload = JsonDocument.Parse(_factory.SapClient.Posts.Single().Body!);
        payload.RootElement.GetProperty("U_sap_id").GetString().Should().Be(key);
        payload.RootElement.GetProperty("CardCode").GetString().Should().Be("P001");
    }

    [Fact]
    public async Task Liquidacion_PostTimesOut_ButSapCreatedIt_ReturnsItRecovered()
    {
        const string key = "cajachica-liquidacion-513";
        var lookups = 0;
        _factory.SapClient.Handler = call =>
        {
            if (IsLookup(call, "PurchaseInvoices", key))
            {
                return Task.FromResult(++lookups == 1
                    ? NotFound
                    : """{"value":[{"DocEntry":9002,"DocNum":9002,"CardCode":"P001","DocumentLines":[{"AccountCode":"_SYS00000000123","LineTotal":25.5}]}]}""");
            }

            if (IsCreate(call, "PurchaseInvoices"))
            {
                throw new TimeoutRejectedException("The operation didn't complete within the allowed timeout of '00:00:30'.");
            }

            throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}");
        };

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/liquidaciones", Liquidacion(key));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(9002);
        body.GetProperty("recovered").GetBoolean().Should().BeTrue();
        _factory.SapClient.Posts.Should().HaveCount(1);
    }

    [Fact]
    public async Task Liquidacion_AccountCodeRejected_StillReturnsTheHintedSapError()
    {
        // The pre-existing AccountCode hint must survive the idempotent flow (lookup finds nothing → rethrow).
        const string key = "cajachica-liquidacion-514";
        _factory.SapClient.Handler = call =>
        {
            if (IsLookup(call, "PurchaseInvoices", key))
            {
                return Task.FromResult(NotFound);
            }

            if (IsCreate(call, "PurchaseInvoices"))
            {
                throw new SapException(HttpStatusCode.BadRequest,
                    """{"error":{"code":-2028,"message":{"lang":"en-us","value":"Invalid G/L account code  [PurchaseInvoices.DocumentLines.AccountCode]"}}}""");
            }

            throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}");
        };

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/liquidaciones", Liquidacion(key));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("OACT.AcctCode");
    }

    [Fact]
    public async Task PurchaseOrder_WithKey_LooksUpFirst_ThenCreatesWithUSapId()
    {
        const string key = "cedi-odc-24748";
        _factory.SapClient.Handler = call => Task.FromResult(call switch
        {
            _ when IsLookup(call, "PurchaseOrders", key) => NotFound,
            _ when IsCreate(call, "PurchaseOrders") => """{"DocEntry":24748,"DocNum":24748}""",
            _ => throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}")
        });

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/purchase-orders", PurchaseOrder(key));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(24748);
        body.GetProperty("recovered").GetBoolean().Should().BeFalse();
        using var payload = JsonDocument.Parse(_factory.SapClient.Posts.Single().Body!);
        payload.RootElement.GetProperty("U_sap_id").GetString().Should().Be(key);
    }

    [Fact]
    public async Task PurchaseOrder_PostTimesOut_ButSapCreatedIt_ReturnsItRecovered()
    {
        const string key = "cedi-odc-24749";
        var lookups = 0;
        _factory.SapClient.Handler = call =>
        {
            if (IsLookup(call, "PurchaseOrders", key))
            {
                return Task.FromResult(++lookups == 1
                    ? NotFound
                    : """{"value":[{"DocEntry":24749,"DocNum":24749,"CardCode":"P001","DocumentLines":[{"ItemCode":"ITEM-001","Quantity":10.0}]}]}""");
            }

            if (IsCreate(call, "PurchaseOrders"))
            {
                throw new TimeoutRejectedException("The operation didn't complete within the allowed timeout of '00:00:30'.");
            }

            throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}");
        };

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/purchase-orders", PurchaseOrder(key));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(24749);
        body.GetProperty("recovered").GetBoolean().Should().BeTrue();
        _factory.SapClient.Posts.Should().HaveCount(1);
    }

    [Fact]
    public async Task Deposit_PostTimesOut_ButSapCreatedIt_ReturnsItRecovered()
    {
        const string key = "depositos-mt-4411";
        var lookups = 0;
        _factory.SapClient.Handler = call =>
        {
            if (IsLookup(call, "InventoryGenEntries", key))
            {
                return Task.FromResult(++lookups == 1
                    ? NotFound
                    : """{"value":[{"DocEntry":72,"DocNum":207,"DocumentLines":[{"ItemCode":"ITEM-001","Quantity":20.0,"WarehouseCode":"WH01"}]}]}""");
            }

            if (IsCreate(call, "InventoryGenEntries"))
            {
                throw new TimeoutRejectedException("The operation didn't complete within the allowed timeout of '00:00:30'.");
            }

            throw new InvalidOperationException($"Unexpected SAP call {call.Method} {call.Url}");
        };

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/deposits", Deposit(key));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("docEntry").GetInt32().Should().Be(72);
        body.GetProperty("recovered").GetBoolean().Should().BeTrue();
        _factory.SapClient.Posts.Should().HaveCount(1);
    }
}
