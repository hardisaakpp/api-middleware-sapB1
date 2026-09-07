using System.Linq;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;
using Middleware.IntegrationTests.Infrastructure;
using Middleware.Infrastructure.WebPos;

namespace Middleware.IntegrationTests.Controllers;

/// <summary>
/// Exercises the real WebPosGateway body-parsing logic end to end: WEBPOS answers business
/// rejections with HTTP 200 just as often as with 400, so success must be decided only by
/// `procesoCorrecto` in the body, never by the transport status code.
/// </summary>
public sealed class WebPosStockTransferControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FakeWebPosClient _fakeWebPosClient = new();

    public WebPosStockTransferControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                Replace<IWebPosClient>(services, _ => _fakeWebPosClient);
                Replace<IWebPosSessionManager>(services, _ => new FakeWebPosSessionManager());
                Replace<IPosSyncStatusRepository>(services, _ => new InMemoryPosSyncStatusRepository());

                var sapMock = new Mock<ISapGateway>();
                sapMock
                    .Setup(g => g.GetStockTransferForTestCompanyAsync(42, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new StockTransferDocumentResponse(42, 1001, "WH01", "WH02",
                        [new StockTransferDocumentLine("ITEM001", 5m, "WH02")]));
                sapMock
                    .Setup(g => g.GetWarehousesPosInfoForTestCompanyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new Dictionary<string, WarehousePosInfo>
                    {
                        ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: true, CodBusiness: "1"),
                        ["WH02"] = new WarehousePosInfo("WH02", IsPosEnabled: true, CodBusiness: "1")
                    });
                sapMock
                    .Setup(g => g.GetItemStockForTestCompanyAsync("ITEM001", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ItemStockResponse("ITEM001", "WH02", 10m, 0m, "UNIDAD", false));

                Replace<ISapGateway>(services, _ => sapMock.Object);
            });
        });
    }

    private static void Replace<TService>(IServiceCollection services, Func<IServiceProvider, TService> factory)
        where TService : class
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TService));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }

        services.AddScoped(factory);
    }

    [Fact]
    public async Task Retry_WebPosAnswers200WithProcesoCorrectoFalse_IsTreatedAsRejectedNotSynced()
    {
        _fakeWebPosClient.NextResponses.Enqueue((
            """{"procesoCorrecto":false,"error":1,"mensaje":"El producto no existe.","retorno":null}""",
            HttpStatusCode.OK));

        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/v1/webpos/stock-transfers/42/retry", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WebPosSyncResult>();
        body!.Status.Should().Be("Rejected");
        body.Reason.Should().Contain("El producto no existe.");
    }

    [Fact]
    public async Task Retry_WebPosAnswers400WithProcesoCorrectoFalse_IsTreatedAsRejected()
    {
        _fakeWebPosClient.NextResponses.Enqueue((
            """{"procesoCorrecto":false,"error":1,"mensaje":"La tienda no pertenece al negocio.","retorno":null}""",
            HttpStatusCode.BadRequest));

        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/v1/webpos/stock-transfers/42/retry", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WebPosSyncResult>();
        body!.Status.Should().Be("Rejected");
        body.Reason.Should().Contain("La tienda no pertenece al negocio.");
    }

    [Fact]
    public async Task Retry_WebPosAnswersProductNotFound_AutoCreatesWithSapEnrichmentAndSucceeds()
    {
        var sapMock = new Mock<ISapGateway>();
        sapMock
            .Setup(g => g.GetStockTransferForTestCompanyAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockTransferDocumentResponse(42, 1001, "WH01", "WH02",
                [new StockTransferDocumentLine("ITEM001", 5m, "WH02")]));
        sapMock
            .Setup(g => g.GetWarehousesPosInfoForTestCompanyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, WarehousePosInfo>
            {
                ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: true, CodBusiness: "1"),
                ["WH02"] = new WarehousePosInfo("WH02", IsPosEnabled: true, CodBusiness: "1")
            });
        sapMock
            .Setup(g => g.GetItemStockForTestCompanyAsync("ITEM001", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemStockResponse("ITEM001", "WH02", 10m, 0m, "UNIDAD", false));
        sapMock
            .Setup(g => g.GetItemPosEnrichmentForTestCompanyAsync("ITEM001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemPosEnrichment("10.270", "rolando", "1", ["786137983", "786137984"]));

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services => Replace<ISapGateway>(services, _ => sapMock.Object));
        }).CreateClient();

        _fakeWebPosClient.NextResponses.Enqueue((
            """{"procesoCorrecto":false,"error":1,"mensaje":"El producto no existe.","retorno":null}""",
            HttpStatusCode.OK));
        _fakeWebPosClient.NextResponses.Enqueue((
            """{"procesoCorrecto":true,"error":0,"mensaje":null,"retorno":null}""",
            HttpStatusCode.OK));

        var response = await client.PostAsync("/api/v1/webpos/stock-transfers/42/retry", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WebPosSyncResult>();
        body!.Status.Should().Be("Synced");

        _fakeWebPosClient.CallCount.Should().Be(2);
        _fakeWebPosClient.CapturedRequestBodies.Should().HaveCount(2);
        _fakeWebPosClient.CapturedRequestBodies.ElementAt(0).Should().NotContain("price");
        var secondRequestBody = _fakeWebPosClient.CapturedRequestBodies.ElementAt(1);
        secondRequestBody.Should().Contain("\"price\":\"10.270\"");
        secondRequestBody.Should().Contain("\"brand\":\"rolando\"");
        secondRequestBody.Should().Contain("786137983");
    }

    [Fact]
    public async Task Retry_SendsStockAsWholeNumber_NeverAsDecimal()
    {
        // HITELL binds `stock` to a java.lang.Integer and rejects "5806.0" (the shape SAP's
        // InStock comes back with) — the wire payload must carry a whole number.
        var sapMock = new Mock<ISapGateway>();
        sapMock
            .Setup(g => g.GetStockTransferForTestCompanyAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockTransferDocumentResponse(42, 1001, "WH01", "WH02",
                [new StockTransferDocumentLine("ITEM001", 1m, "WH02")]));
        sapMock
            .Setup(g => g.GetWarehousesPosInfoForTestCompanyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, WarehousePosInfo>
            {
                ["WH02"] = new WarehousePosInfo("WH02", IsPosEnabled: true, CodBusiness: "1")
            });
        sapMock
            .Setup(g => g.GetItemStockForTestCompanyAsync("ITEM001", "WH02", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemStockResponse("ITEM001", "WH02", 5806.0m, 0m, "UNIDAD", false));

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services => Replace<ISapGateway>(services, _ => sapMock.Object));
        }).CreateClient();

        _fakeWebPosClient.NextResponses.Enqueue((
            """{"procesoCorrecto":true,"error":0,"mensaje":null,"retorno":null}""",
            HttpStatusCode.OK));

        var response = await client.PostAsync("/api/v1/webpos/stock-transfers/42/retry", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var requestBody = _fakeWebPosClient.CapturedRequestBodies.Single();
        requestBody.Should().Contain("\"stock\":5806,");
        requestBody.Should().NotContain("5806.0");
    }

    [Fact]
    public async Task Retry_WebPosAnswers200WithProcesoCorrectoTrue_IsTreatedAsSynced()
    {
        _fakeWebPosClient.NextResponses.Enqueue((
            """{"procesoCorrecto":true,"error":0,"mensaje":null,"retorno":null}""",
            HttpStatusCode.OK));

        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/v1/webpos/stock-transfers/42/retry", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WebPosSyncResult>();
        body!.Status.Should().Be("Synced");
    }

    private sealed record WebPosSyncResult(string Status, string? Reason, Guid? JobId);
}
