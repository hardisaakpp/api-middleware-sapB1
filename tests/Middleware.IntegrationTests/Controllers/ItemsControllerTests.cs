using System.Net;
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
            "U_IXX_PACK": "PACK-12"
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
        url.StartsWith("ItemGroups", StringComparison.Ordinal) ? ItemGroupsJson : FullItemJson;

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
        fake.Urls.Count(u => u.StartsWith("Items(", StringComparison.Ordinal))
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

    private sealed class ScriptedSapClient : ISapServiceLayerClient
    {
        private readonly Func<string, string> _responder;
        private readonly List<string> _urls = [];

        public ScriptedSapClient(Func<string, string> responder) => _responder = responder;

        public IReadOnlyList<string> Urls
        {
            get { lock (_urls) { return _urls.ToList(); } }
        }

        public Task<string> SendAsync(
            HttpMethod method,
            string relativeUrl,
            string? body,
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            lock (_urls) { _urls.Add(relativeUrl); }
            return Task.FromResult(_responder(relativeUrl));
        }
    }
}

file sealed class StubSessionManager : ISapSessionManager
{
    public Task<string> GetSessionTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult("fake-session-token");

    public Task<string> GetSessionTokenAsync(string companyDb, string password, CancellationToken cancellationToken = default)
        => Task.FromResult("fake-session-token");

    public Task InvalidateSessionAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InvalidateSessionAsync(string companyDb, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
