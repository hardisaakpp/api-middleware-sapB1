using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Domain.Enums;

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
                mock.Setup(g => g.CreateStockTransferBasedOnDocumentAsync(
                        It.IsAny<CreateStockTransferBasedOnDocumentRequest>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DocumentCreatedResult(44, 1003));
                mock.Setup(g => g.CreateStockTransferDirectAsync(
                        It.IsAny<CreateStockTransferDirectRequest>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DocumentCreatedResult(45, 1004));
                mock.Setup(g => g.CreateStockTransferWithBinsAsync(
                        It.IsAny<CreateStockTransferWithBinsRequest>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DocumentCreatedResult(46, 1005));

                services.AddScoped<ISapGateway>(_ => mock.Object);

                // These tests are only about the SAP-creation contract — replace the WEBPOS
                // notifier with a fixed stub so they don't depend on WEBPOS/SQL infrastructure.
                var notifierDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IWebPosStockTransferNotifier));
                if (notifierDescriptor is not null)
                {
                    services.Remove(notifierDescriptor);
                }

                var notifierMock = new Mock<IWebPosStockTransferNotifier>();
                notifierMock
                    .Setup(n => n.NotifyAsync(
                        It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                        It.IsAny<IReadOnlyList<NotifyWebPosStockTransferLine>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new WebPosSyncInfo(WebPosSyncStatus.Synced));

                services.AddScoped<IWebPosStockTransferNotifier>(_ => notifierMock.Object);
            });
        });
    }

    [Fact]
    public async Task Post_StockTransferBasedOnDocument_Returns201WithDocumentNumbers()
    {
        var client = _factory.CreateClient();

        var request = new CreateStockTransferBasedOnDocumentRequest(
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
            StockTransferLines: [new StockTransferBasedOnDocumentLineDto(17, 123, 0, "ITEM001", 10m, "WH02")]
        );

        var response = await client.PostAsJsonAsync("/api/v1/stock-transfers/based-on-document", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<StockTransferResult>();
        body!.DocEntry.Should().Be(44);
        body.DocNum.Should().Be(1003);
    }

    [Fact]
    public async Task Post_StockTransferBasedOnDocument_SetsCorrelationIdHeader()
    {
        var client = _factory.CreateClient();

        var request = new CreateStockTransferBasedOnDocumentRequest(
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
            StockTransferLines: [new StockTransferBasedOnDocumentLineDto(null, null, null, "ITEM001", 5m, "WH02")]
        );

        var response = await client.PostAsJsonAsync("/api/v1/stock-transfers/based-on-document", request);

        response.Headers.Should().ContainKey("X-Correlation-Id");
    }

    [Fact]
    public async Task Post_StockTransferDirect_Returns201WithDocumentNumbers()
    {
        var client = _factory.CreateClient();

        var request = new CreateStockTransferDirectRequest(
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
            StockTransferLines: [new StockTransferDirectLineDto("ITEM001", 10m, "WH02")],
            U_sap_id: null,
            U_GUIA_LAAR: null
        );

        var response = await client.PostAsJsonAsync("/api/v1/stock-transfers/direct", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<StockTransferResult>();
        body!.DocEntry.Should().Be(45);
        body.DocNum.Should().Be(1004);
    }

    [Fact]
    public async Task Post_StockTransferWithBins_Returns201WithDocumentNumbers()
    {
        var client = _factory.CreateClient();

        var request = new CreateStockTransferWithBinsRequest(
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
            StockTransferLines:
            [
                new StockTransferWithBinsLineDto(
                    BaseType: null,
                    BaseEntry: null,
                    BaseLine: null,
                    ItemCode: "ITEM001",
                    Quantity: 10m,
                    WarehouseCode: "WH02",
                    StockTransferLinesBinAllocations:
                    [
                        new StockTransferBinAllocationDto(BinAbsEntry: 5, Quantity: 10m, BinActionType: 2),
                        new StockTransferBinAllocationDto(BinAbsEntry: 9, Quantity: 10m, BinActionType: 1)
                    ])
            ]
        );

        var response = await client.PostAsJsonAsync("/api/v1/stock-transfers/with-bins", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<StockTransferResult>();
        body!.DocEntry.Should().Be(46);
        body.DocNum.Should().Be(1005);
    }

    private sealed record StockTransferResult(int DocEntry, int DocNum);
}
