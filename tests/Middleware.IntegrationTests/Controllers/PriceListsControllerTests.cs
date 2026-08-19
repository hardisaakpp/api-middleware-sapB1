using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.IntegrationTests.Controllers;

public sealed class PriceListsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public PriceListsControllerTests(WebApplicationFactory<Program> factory)
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
    public async Task Get_ReturnsFullPriceListCatalog()
    {
        var client = CreateClient(mock =>
            mock.Setup(g => g.GetPriceListsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new PriceListResponse(1, "Lista Minorista"),
                    new PriceListResponse(2, "Lista Internacional")
                ]));

        var response = await client.GetAsync("/api/v1/price-lists");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PriceListsResponse>();
        body!.PriceLists.Should().HaveCount(2);
        body.PriceLists[0].PriceListNo.Should().Be(1);
        body.PriceLists[0].PriceListName.Should().Be("Lista Minorista");
    }

    [Fact]
    public async Task Get_ServiceLayerFailure_Returns5xx()
    {
        var client = CreateClient(mock =>
            mock.Setup(g => g.GetPriceListsAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Service Layer unreachable")));

        var response = await client.GetAsync("/api/v1/price-lists");

        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(500);
    }
}
