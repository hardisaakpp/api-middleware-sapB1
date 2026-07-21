using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;
using Middleware.Infrastructure.Sap;

namespace Middleware.IntegrationTests.Controllers;

public sealed class ItemsBatchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public ItemsBatchEndpointTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    private HttpClient CreateClient(Action<Mock<ISapGateway>> configureMock)
    {
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISapGateway));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                var mock = new Mock<ISapGateway>();
                configureMock(mock);
                services.AddScoped<ISapGateway>(_ => mock.Object);
            });
        });
        return factory.CreateClient();
    }

    private static ItemResponse Item(string code) =>
        new(code, $"Nombre {code}", InventoryUoM: null, OnHand: 179m,
            PurchaseItem: true, SalesItem: true, InventoryItem: true, Valid: true, Frozen: false,
            ItemType: "itItems", PackCode: "X3", GroupCode: 103, GroupName: "R-MEDIAS",
            PreferredVendorCode: "P001");

    [Fact]
    public async Task Post_MixOfExistingAndMissing_ReturnsItemsAndNotFound()
    {
        var client = CreateClient(mock =>
            mock.Setup(g => g.GetItemsByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<string> chunk, CancellationToken _) =>
                    chunk.Where(c => !c.StartsWith("NO-")).Select(Item).ToList()));

        var response = await client.PostAsJsonAsync("/api/v1/items/batch",
            new { itemCodes = new[] { "R215.810.NO", "780-2404174", "NO-EXISTE-123" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("groupName").GetString().Should().Be("R-MEDIAS");
        var notFound = body.RootElement.GetProperty("notFound");
        notFound.GetArrayLength().Should().Be(1);
        notFound[0].GetString().Should().Be("NO-EXISTE-123");
    }

    [Fact]
    public async Task Post_EmptyList_Returns200WithEmptyArrays()
    {
        var client = CreateClient(_ => { });

        var response = await client.PostAsJsonAsync("/api/v1/items/batch",
            new { itemCodes = Array.Empty<string>() });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        body.RootElement.GetProperty("notFound").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Post_MoreThan100Codes_Returns400()
    {
        var client = CreateClient(_ => { });

        var codes = Enumerable.Range(1, 101).Select(i => $"ITEM{i:D3}").ToArray();
        var response = await client.PostAsJsonAsync("/api/v1/items/batch", new { itemCodes = codes });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_ServiceLayerFailure_FailsWholeRequestWith5xx()
    {
        var client = CreateClient(mock =>
            mock.Setup(g => g.GetItemsByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SapThrottleException("SAP throttle wait exhausted")));

        var response = await client.PostAsJsonAsync("/api/v1/items/batch",
            new { itemCodes = new[] { "A", "B" } });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
