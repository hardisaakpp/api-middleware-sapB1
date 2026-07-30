using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;

namespace Middleware.IntegrationTests.Controllers;

public sealed class StockTransferControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public StockTransferControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace ISapGateway with a mock — no real SAP calls in integration tests
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISapGateway));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                var mock = new Mock<ISapGateway>();
                mock.Setup(g => g.CreateStockTransferTestAsync(
                        It.IsAny<CreateStockTransferTestRequest>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DocumentCreatedResult(44, 1003));

                services.AddScoped<ISapGateway>(_ => mock.Object);
            });
        });
    }

    [Fact]
    public async Task Post_StockTransferTest_Returns201WithDocumentNumbers()
    {
        var client = _factory.CreateClient();

        var request = new CreateStockTransferTestRequest(
            DocObjectCode: null,
            CardCode: null,
            DocDate: "2024-06-01",
            TaxDate: null,
            Comments: null,
            FromWarehouse: "WH01",
            ToWarehouse: "WH02",
            U_ORIGEN: null,
            Series: null,
            PriceList: null,
            StockTransferLines: [new StockTransferTestLineDto(null, null, null, "ITEM001", 10m, "WH02")]
        );

        var response = await client.PostAsJsonAsync("/api/v1/stock-transfers/test", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<StockTransferResult>();
        body!.DocEntry.Should().Be(44);
        body.DocNum.Should().Be(1003);
    }

    [Fact]
    public async Task Post_StockTransferTest_SetsCorrelationIdHeader()
    {
        var client = _factory.CreateClient();

        var request = new CreateStockTransferTestRequest(
            DocObjectCode: null,
            CardCode: null,
            DocDate: "2024-06-01",
            TaxDate: null,
            Comments: null,
            FromWarehouse: "WH01",
            ToWarehouse: "WH02",
            U_ORIGEN: null,
            Series: null,
            PriceList: null,
            StockTransferLines: [new StockTransferTestLineDto(null, null, null, "ITEM001", 5m, "WH02")]
        );

        var response = await client.PostAsJsonAsync("/api/v1/stock-transfers/test", request);

        response.Headers.Should().ContainKey("X-Correlation-Id");
    }

    private sealed record StockTransferResult(int DocEntry, int DocNum);
}
