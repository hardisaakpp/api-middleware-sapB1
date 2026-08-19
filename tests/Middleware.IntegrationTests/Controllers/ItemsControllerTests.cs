using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Infrastructure.Sap;

namespace Middleware.IntegrationTests.Controllers;

/// <summary>
/// Exercises GET /api/v1/items/{itemCode} through the REAL SapGateway parsing,
/// replacing only the low-level SL HTTP client with a scripted fake.
/// </summary>
public sealed class ItemsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ItemGroupsJson = """
        {
            "value": [
                { "Number": 105, "GroupName": "R-MEDIAS" },
                { "Number": 344, "GroupName": "RD-INDUM MEDIAS" }
            ]
        }
        """;

    // ItemPrices ya NO viene en la respuesta base del ítem: SAP rechaza
    // $expand=ItemPrices con -1000 "Cannot expand invalid navigation property"
    // (verificado en vivo contra Service Layer real el 2026-08-07). Solo es
    // accesible como sub-recurso propio, Items('CODE')/ItemPrices — ver
    // ItemPricesJson y GetPrecioMinoristaAsync en SapGateway.
    private const string FullItemJson = """
        {
            "ItemCode": "R190.1012",
            "ItemName": "CASUAL LISA X1",
            "InventoryUOM": "UNIDAD",
            "InventoryUoMEntry": -1,
            "QuantityOnStock": 10.0,
            "PurchaseItem": "tYES",
            "SalesItem": "tYES",
            "InventoryItem": "tYES",
            "Valid": "tYES",
            "Frozen": "tNO",
            "ItemType": "itItems",
            "ItemsGroupCode": 105,
            "Mainsupplier": "P1790389219001",
            "U_IXX_PACK": "PACK-12",
            "BarCode": "7861215678025"
        }
        """;

    private const string ItemPricesJson = """
        {
            "ItemPrices": [
                { "PriceList": 2, "Price": 9.99, "Currency": "USD" },
                { "PriceList": 1, "Price": 6.99, "Currency": "USD" }
            ]
        }
        """;

    private const string ItemWithoutOptionalsJson = """
        {
            "ItemCode": "R190.1012",
            "ItemName": "CASUAL LISA X1",
            "QuantityOnStock": 10.0,
            "PurchaseItem": "tYES",
            "SalesItem": "tNO",
            "InventoryItem": "tYES",
            "Valid": "tYES",
            "Frozen": "tNO",
            "ItemType": "itItems",
            "ItemsGroupCode": 999,
            "Mainsupplier": "",
            "U_IXX_PACK": null
        }
        """;

    private readonly WebApplicationFactory<Program> _baseFactory;

    public ItemsControllerTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    private HttpClient CreateClient(ScriptedSapClient fakeClient)
    {
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var clientDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISapServiceLayerClient));
                if (clientDescriptor is not null)
                {
                    services.Remove(clientDescriptor);
                }
                services.AddSingleton<ISapServiceLayerClient>(_ => fakeClient);

                var sessionDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISapSessionManager));
                if (sessionDescriptor is not null)
                {
                    services.Remove(sessionDescriptor);
                }
                services.AddSingleton<ISapSessionManager>(_ => new StubSessionManager());
            });
        });
        return factory.CreateClient();
    }

    private static string DefaultResponder(string url) =>
        url.StartsWith("ItemGroups", StringComparison.Ordinal) ? ItemGroupsJson
        : url.Contains("/ItemPrices", StringComparison.Ordinal) ? ItemPricesJson
        : FullItemJson;

    [Fact]
    public async Task Get_ItemWithPackAndPreferredVendor_ReturnsExtendedFields()
    {
        var fake = new ScriptedSapClient(DefaultResponder);
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/R190.1012");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        root.GetProperty("itemCode").GetString().Should().Be("R190.1012");
        root.GetProperty("inventoryUoM").GetString().Should().Be("UNIDAD");
        root.GetProperty("purchaseItem").GetBoolean().Should().BeTrue();
        root.GetProperty("salesItem").GetBoolean().Should().BeTrue();
        root.GetProperty("inventoryItem").GetBoolean().Should().BeTrue();
        root.GetProperty("valid").GetBoolean().Should().BeTrue();
        root.GetProperty("frozen").GetBoolean().Should().BeFalse();
        root.GetProperty("itemType").GetString().Should().Be("itItems");
        root.GetProperty("packCode").GetString().Should().Be("PACK-12");
        root.GetProperty("groupCode").GetInt32().Should().Be(105);
        root.GetProperty("groupName").GetString().Should().Be("R-MEDIAS");
        root.GetProperty("preferredVendorCode").GetString().Should().Be("P1790389219001");
        root.GetProperty("codeBars").GetString().Should().Be("7861215678025");
        root.GetProperty("pvp").GetDecimal().Should().Be(6.99m,
            "PriceList 1 is 'Precio Minorista'; the 9.99 entry (PriceList 2) must be ignored");
    }

    [Fact]
    public async Task Get_ItemWithUnknownGroupAndNoOptionals_OmitsNullFields()
    {
        // ItemsGroupCode 999 is not in the ItemGroups map → groupName null → omitted
        var fake = new ScriptedSapClient(url =>
            url.StartsWith("ItemGroups", StringComparison.Ordinal) ? ItemGroupsJson : ItemWithoutOptionalsJson);
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/R190.1012");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        root.GetProperty("salesItem").GetBoolean().Should().BeFalse();
        root.GetProperty("groupCode").GetInt32().Should().Be(999);
        // Null values are omitted from JSON (API-wide policy)
        root.TryGetProperty("groupName", out _).Should().BeFalse();
        root.TryGetProperty("packCode", out _).Should().BeFalse();
        root.TryGetProperty("preferredVendorCode", out _).Should().BeFalse();
        root.TryGetProperty("codeBars", out _).Should().BeFalse();
        root.TryGetProperty("pvp", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Get_ItemPricesWithoutPrecioMinoristaEntry_OmitsPvp()
    {
        // The item has other price lists configured, but none is PriceList 1 ("Precio
        // Minorista") — pvp must come back omitted, not accidentally pick another list's price.
        const string itemWithoutPriceListJson = """
            {
                "ItemCode": "R190.1012",
                "ItemName": "CASUAL LISA X1",
                "QuantityOnStock": 10.0,
                "PurchaseItem": "tYES",
                "SalesItem": "tYES",
                "InventoryItem": "tYES",
                "Valid": "tYES",
                "Frozen": "tNO",
                "ItemType": "itItems",
                "ItemsGroupCode": 105
            }
            """;
        const string pricesWithoutPrecioMinoristaJson = """
            {
                "ItemPrices": [
                    { "PriceList": 2, "Price": 9.99, "Currency": "USD" },
                    { "PriceList": 3, "Price": 12.50, "Currency": "USD" }
                ]
            }
            """;
        var fake = new ScriptedSapClient(url =>
            url.StartsWith("ItemGroups", StringComparison.Ordinal) ? ItemGroupsJson
            : url.Contains("/ItemPrices", StringComparison.Ordinal) ? pricesWithoutPrecioMinoristaJson
            : itemWithoutPriceListJson);
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/R190.1012");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.TryGetProperty("pvp", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Get_ItemPricesSubresourceRejectedBySap_OmitsPvpWithoutFailingTheItem()
    {
        // Regresión del incidente 2026-08-07: SAP puede rechazar la consulta de precio
        // (aquí, con el mismo error real -1000 que causó el bug original) para un ítem
        // que de otro modo existe y resuelve bien. pvp debe quedar omitido; el resto del
        // ítem no debe verse afectado por esa falla.
        var fake = new ScriptedSapClient(url =>
            url.StartsWith("ItemGroups", StringComparison.Ordinal) ? ItemGroupsJson
            : url.Contains("/ItemPrices", StringComparison.Ordinal)
                ? throw new SapException(HttpStatusCode.BadRequest, "SAP rejected the ItemPrices sub-resource")
                : FullItemJson);
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/R190.1012");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("itemCode").GetString().Should().Be("R190.1012");
        body.RootElement.TryGetProperty("pvp", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Get_Item_NeverSendsExpandItemPrices()
    {
        // Regresión directa del incidente 2026-08-07: SAP rechaza $expand=ItemPrices
        // con -1000 "Cannot expand invalid navigation property" — verificado en vivo
        // contra Service Layer real. El precio se resuelve por el sub-recurso propio
        // (Items('CODE')/ItemPrices), nunca por $expand en la consulta del ítem base.
        var fake = new ScriptedSapClient(DefaultResponder);
        var client = CreateClient(fake);

        await client.GetAsync("/api/v1/items/R190.1012");

        fake.Urls.Should().NotContain(
            u => u.Contains("$expand", StringComparison.OrdinalIgnoreCase),
            "SAP rejects $expand=ItemPrices; el precio se obtiene del sub-recurso Items('CODE')/ItemPrices"
        );
        fake.Urls.Should().Contain(
            u => u.StartsWith("Items(", StringComparison.Ordinal) && u.Contains("/ItemPrices", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Get_Item_NeverSelectsCodeBars()
    {
        // Regresión directa del incidente 2026-08-07: SAP rechaza $select=CodeBars con
        // -1000 "Property 'CodeBars' of 'Item' is invalid" — verificado en vivo. El
        // campo real es "BarCode" (singular); "codeBars" sigue siendo el nombre de la
        // propiedad JSON de ESTA API (contrato externo sin cambios), solo cambió qué
        // se le pide a SAP internamente.
        var fake = new ScriptedSapClient(DefaultResponder);
        var client = CreateClient(fake);

        await client.GetAsync("/api/v1/items/R190.1012");

        fake.Urls.Should().NotContain(u => u.Contains("CodeBars", StringComparison.Ordinal));
        fake.Urls.Should().Contain(u => u.Contains("BarCode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get_CompanyDbWithoutPackUdf_RetriesWithoutUdfAndReturnsItem()
    {
        var fake = new ScriptedSapClient(url =>
            url.StartsWith("ItemGroups", StringComparison.Ordinal) ? ItemGroupsJson
            : url.Contains("U_IXX_PACK")
                ? throw new SapException(HttpStatusCode.BadRequest, "Invalid property 'U_IXX_PACK'")
                : ItemWithoutOptionalsJson);
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/R190.1012");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Excluye la llamada a /ItemPrices (sin $select, no forma parte del retry de UDF)
        fake.Urls.Count(u => u.StartsWith("Items(", StringComparison.Ordinal) && u.Contains("$select", StringComparison.Ordinal))
            .Should().Be(2, "the gateway must retry the $select without the UDF");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("itemCode").GetString().Should().Be("R190.1012");
        body.RootElement.TryGetProperty("packCode", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetStock_WarehouseWithCommitted_ReturnsInStockIsCommittedUomCodeAndLocked()
    {
        const string stockJson = """
            {
                "ItemCode": "R215.810.NO",
                "InventoryUOM": "UN",
                "ItemWarehouseInfoCollection": [
                    { "WarehouseCode": "RL-CD", "InStock": 0.0, "Committed": 292.0, "Locked": "tYES" }
                ]
            }
            """;
        var fake = new ScriptedSapClient(_ => stockJson);
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/R215.810.NO/stock/RL-CD");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("inStock").GetDecimal().Should().Be(0.0m);
        body.RootElement.GetProperty("isCommitted").GetDecimal().Should().Be(292.0m);
        body.RootElement.GetProperty("uomCode").GetString().Should().Be("UN");
        body.RootElement.GetProperty("locked").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetStock_WarehouseWithoutCommittedField_ReturnsZero()
    {
        const string stockJson = """
            {
                "ItemCode": "R215.810.NO",
                "ItemWarehouseInfoCollection": [
                    { "WarehouseCode": "RL-CD", "InStock": 50.0 }
                ]
            }
            """;
        var fake = new ScriptedSapClient(_ => stockJson);
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/R215.810.NO/stock/RL-CD");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("inStock").GetDecimal().Should().Be(50.0m);
        body.RootElement.GetProperty("isCommitted").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task GetStock_WarehouseNotInCollection_ReturnsBothZeroAndUnlocked()
    {
        const string stockJson = """
            {
                "ItemCode": "R215.810.NO",
                "InventoryUOM": "UN",
                "ItemWarehouseInfoCollection": [
                    { "WarehouseCode": "OTRA-BODEGA", "InStock": 999.0, "Committed": 111.0, "Locked": "tYES" }
                ]
            }
            """;
        var fake = new ScriptedSapClient(_ => stockJson);
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/R215.810.NO/stock/RL-CD");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("inStock").GetDecimal().Should().Be(0m);
        body.RootElement.GetProperty("isCommitted").GetDecimal().Should().Be(0m);
        // Item-level UoM still resolves even when the requested warehouse has no entry
        body.RootElement.GetProperty("uomCode").GetString().Should().Be("UN");
        body.RootElement.GetProperty("locked").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Get_ItemGroupsLoadFails_StillReturnsItemWithoutGroupName()
    {
        var fake = new ScriptedSapClient(url =>
            url.StartsWith("ItemGroups", StringComparison.Ordinal)
                ? throw new SapException(HttpStatusCode.InternalServerError, "SL error")
                : FullItemJson);
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/R190.1012");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("groupCode").GetInt32().Should().Be(105);
        body.RootElement.TryGetProperty("groupName", out _).Should().BeFalse();
    }

    // Estos tests ejercitan POST /api/v1/items/batch a través del SapGateway REAL
    // (a diferencia de ItemsBatchEndpointTests.cs, que mockea ISapGateway y por lo
    // tanto nunca ejercita la construcción real de la consulta a SAP). Es exactamente
    // el hueco de cobertura que dejó pasar el incidente 2026-08-07: $expand=ItemPrices
    // funcionaba contra fixtures pero SAP real lo rechazaba con -1000.
    [Fact]
    public async Task Post_Batch_ResolvesPvpPerItemViaIndividualCalls()
    {
        const string batchBaseJson = """
            {
                "value": [
                    { "ItemCode": "R311.AMAZ-4-1012", "ItemName": "COLORS AMAZ", "ItemsGroupCode": 105, "Valid": "tYES", "Frozen": "tNO", "ItemType": "itItems" },
                    { "ItemCode": "R311.SIE-20.1012", "ItemName": "COLORS SIE", "ItemsGroupCode": 105, "Valid": "tYES", "Frozen": "tNO", "ItemType": "itItems" }
                ]
            }
            """;
        const string pricesA = """{ "ItemPrices": [ { "PriceList": 1, "Price": 6.99, "Currency": "USD" } ] }""";
        const string pricesB = """{ "ItemPrices": [ { "PriceList": 1, "Price": 3.99, "Currency": "USD" } ] }""";

        var fake = new ScriptedSapClient(url =>
            url.StartsWith("ItemGroups", StringComparison.Ordinal) ? ItemGroupsJson
            : url.Contains("Items('R311.AMAZ-4-1012')/ItemPrices", StringComparison.Ordinal) ? pricesA
            : url.Contains("Items('R311.SIE-20.1012')/ItemPrices", StringComparison.Ordinal) ? pricesB
            : batchBaseJson);
        var client = CreateClient(fake);

        var response = await client.PostAsJsonAsync("/api/v1/items/batch",
            new { itemCodes = new[] { "R311.AMAZ-4-1012", "R311.SIE-20.1012" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        items.Single(i => i.GetProperty("itemCode").GetString() == "R311.AMAZ-4-1012")
            .GetProperty("pvp").GetDecimal().Should().Be(6.99m);
        items.Single(i => i.GetProperty("itemCode").GetString() == "R311.SIE-20.1012")
            .GetProperty("pvp").GetDecimal().Should().Be(3.99m);
    }

    [Fact]
    public async Task Post_Batch_NeverSendsExpandItemPrices()
    {
        var fake = new ScriptedSapClient(DefaultResponder);
        var client = CreateClient(fake);

        await client.PostAsJsonAsync("/api/v1/items/batch", new { itemCodes = new[] { "R190.1012" } });

        fake.Urls.Should().NotContain(
            u => u.Contains("$expand", StringComparison.OrdinalIgnoreCase),
            "SAP rejects $expand=ItemPrices even en la consulta batch; el precio se obtiene por ítem via el sub-recurso"
        );
    }

    [Fact]
    public async Task Post_Batch_OneItemPricesCallFails_OtherItemStillResolvesPvpAndBatchSucceeds()
    {
        // Que SAP rechace el precio de UN ítem (mismo error real -1000 del incidente)
        // no debe tumbar el batch completo ni afectar al resto de ítems resueltos.
        const string batchBaseJson = """
            {
                "value": [
                    { "ItemCode": "CON-PRECIO", "ItemName": "Con precio", "ItemsGroupCode": 105, "Valid": "tYES", "Frozen": "tNO", "ItemType": "itItems" },
                    { "ItemCode": "SIN-PRECIO", "ItemName": "Sin precio", "ItemsGroupCode": 105, "Valid": "tYES", "Frozen": "tNO", "ItemType": "itItems" }
                ]
            }
            """;
        const string preciosOk = """{ "ItemPrices": [ { "PriceList": 1, "Price": 6.99, "Currency": "USD" } ] }""";

        var fake = new ScriptedSapClient(url =>
            url.StartsWith("ItemGroups", StringComparison.Ordinal) ? ItemGroupsJson
            : url.Contains("Items('CON-PRECIO')/ItemPrices", StringComparison.Ordinal) ? preciosOk
            : url.Contains("Items('SIN-PRECIO')/ItemPrices", StringComparison.Ordinal)
                ? throw new SapException(HttpStatusCode.BadRequest, "Cannot expand invalid navigation property 'ItemPrices'")
            : batchBaseJson);
        var client = CreateClient(fake);

        var response = await client.PostAsJsonAsync("/api/v1/items/batch",
            new { itemCodes = new[] { "CON-PRECIO", "SIN-PRECIO" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2, "un fallo al resolver el precio de un ítem no debe tumbar el batch");
        items.Single(i => i.GetProperty("itemCode").GetString() == "CON-PRECIO")
            .GetProperty("pvp").GetDecimal().Should().Be(6.99m);
        items.Single(i => i.GetProperty("itemCode").GetString() == "SIN-PRECIO")
            .TryGetProperty("pvp", out _).Should().BeFalse();
    }

    private sealed class ScriptedSapClient : ISapServiceLayerClient
    {
        private readonly Func<string, string> _responder;
        private readonly List<string> _urls = [];

        public ScriptedSapClient(Func<string, string> responder) => _responder = responder;

        public IReadOnlyList<string> Urls
        {
            get { lock (_urls) return _urls.ToList(); }
        }

        public Task<string> SendAsync(
            HttpMethod method,
            string relativeUrl,
            string? body,
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            lock (_urls) _urls.Add(relativeUrl);
            return Task.FromResult(_responder(relativeUrl));
        }
    }
}

file sealed class StubSessionManager : ISapSessionManager
{
    public Task<string> GetSessionTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult("fake-session-token");

    public Task InvalidateSessionAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
