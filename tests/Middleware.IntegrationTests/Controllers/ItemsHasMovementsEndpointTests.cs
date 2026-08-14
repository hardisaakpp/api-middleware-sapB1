using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.IntegrationTests.Controllers;

/// <summary>
/// Covers GET /api/v1/items/{itemCode}/has-movements — the combined existence check that replaces
/// api-articulo-maestro's own fan-out to PurchaseOrders/InventoryGenEntries/InventoryTransferRequests.
/// </summary>
public sealed class ItemsHasMovementsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public ItemsHasMovementsEndpointTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task Get_NoMovementsInAnySource_ReturnsFalse()
    {
        var client = CreateClient(mock =>
        {
            mock.Setup(g => g.HasPurchaseOrderLineForItemAsync("R190.1012", It.IsAny<CancellationToken>())).ReturnsAsync(false);
            mock.Setup(g => g.HasInventoryGenEntryLineForItemAsync("R190.1012", It.IsAny<CancellationToken>())).ReturnsAsync(false);
            mock.Setup(g => g.HasInventoryTransferRequestLineForItemAsync("R190.1012", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        });

        var response = await client.GetAsync("/api/v1/items/R190.1012/has-movements");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HasItemMovementsResponse>();
        body!.HasMovements.Should().BeFalse();
    }

    [Fact]
    public async Task Get_MovementsInPurchaseOrdersOnly_ReturnsTrueWithBreakdown()
    {
        var client = CreateClient(mock =>
        {
            mock.Setup(g => g.HasPurchaseOrderLineForItemAsync("R190.1012", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            mock.Setup(g => g.HasInventoryGenEntryLineForItemAsync("R190.1012", It.IsAny<CancellationToken>())).ReturnsAsync(false);
            mock.Setup(g => g.HasInventoryTransferRequestLineForItemAsync("R190.1012", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        });

        var response = await client.GetAsync("/api/v1/items/R190.1012/has-movements");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HasItemMovementsResponse>();
        body!.HasMovements.Should().BeTrue();
        body.HasPurchaseOrders.Should().BeTrue();
        body.HasInventoryGenEntries.Should().BeFalse();
        body.HasInventoryTransferRequests.Should().BeFalse();
    }

    [Fact]
    public async Task Get_ServiceLayerFailure_Returns5xx()
    {
        var client = CreateClient(mock =>
            mock.Setup(g => g.HasPurchaseOrderLineForItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Service Layer unreachable")));

        var response = await client.GetAsync("/api/v1/items/R190.1012/has-movements");

        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(500);
    }
}
