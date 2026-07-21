using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.IntegrationTests.Controllers;

public sealed class ValidationsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public ValidationsControllerTests(WebApplicationFactory<Program> factory)
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

    private static SapItemValidationData Item(string code) =>
        new(code, $"Nombre {code}", PurchaseItem: true, Valid: true, Frozen: false,
            InventoryUoM: null,
            Warehouses: [new SapItemWarehouseData("WH01", 10m, 2m, Locked: false)]);

    private static object Payload(string? cardCode, int lineCount, string prefix = "ITEM") => new
    {
        cardCode,
        lines = Enumerable.Range(1, lineCount)
            .Select(i => new { itemCode = $"{prefix}{i:D3}", warehouseCode = "WH01" })
            .ToArray()
    };

    [Fact]
    public async Task Post_PartialChunkFailure_Returns200WithConnectionErrorsOnlyForFailedChunk()
    {
        // Default chunk size is 20 → 25 items produce 2 chunks; the second one (ITEM021+) fails
        var client = CreateClient(mock =>
        {
            mock.Setup(g => g.GetItemsForValidationAsync(
                    It.Is<IReadOnlyList<string>>(c => c.Contains("ITEM021")),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Service Layer unreachable"));
            mock.Setup(g => g.GetItemsForValidationAsync(
                    It.Is<IReadOnlyList<string>>(c => !c.Contains("ITEM021")),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<string> chunk, CancellationToken _) =>
                    chunk.Select(Item).ToList());
        });

        var response = await client.PostAsJsonAsync("/api/v1/validations/purchase-order", Payload(null, 25));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ValidatePurchaseOrderResponse>();
        body!.Items.Should().HaveCount(25);
        body.Items.Where(i => int.Parse(i.ItemCode[4..]) <= 20)
            .Should().OnlyContain(i => i.ErrorType == null && i.Exists && i.WarehouseConfigured);
        body.Items.Where(i => int.Parse(i.ItemCode[4..]) > 20)
            .Should().OnlyContain(i => i.ErrorType == "connection_error" && !i.Exists);
    }

    [Fact]
    public async Task Post_WithCardCode_ValidatesSupplierAndItemsInOneCall()
    {
        var client = CreateClient(mock =>
        {
            mock.Setup(g => g.GetItemsForValidationAsync(
                    It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<string> chunk, CancellationToken _) =>
                    chunk.Select(Item).ToList());
            mock.Setup(g => g.GetBusinessPartnerAsync("P001", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BusinessPartnerResponse(
                    "P001", "PROVEEDOR X", "cSupplier", null, null, null, Valid: true, Frozen: false));
        });

        var response = await client.PostAsJsonAsync("/api/v1/validations/purchase-order", Payload("P001", 3));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ValidatePurchaseOrderResponse>();
        body!.BusinessPartner!.Exists.Should().BeTrue();
        body.BusinessPartner.CardName.Should().Be("PROVEEDOR X");
        body.Items.Should().HaveCount(3).And.OnlyContain(i => i.ErrorType == null);
    }

    [Fact]
    public async Task Post_TotalServiceLayerFailure_Returns503()
    {
        var client = CreateClient(mock =>
        {
            mock.Setup(g => g.GetItemsForValidationAsync(
                    It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Service Layer unreachable"));
        });

        var response = await client.PostAsJsonAsync("/api/v1/validations/purchase-order", Payload(null, 5));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Post_MoreThan500Lines_Returns400()
    {
        var client = CreateClient(_ => { });

        var response = await client.PostAsJsonAsync("/api/v1/validations/purchase-order", Payload(null, 501));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_EmptyLines_Returns400()
    {
        var client = CreateClient(_ => { });

        var response = await client.PostAsJsonAsync("/api/v1/validations/purchase-order", Payload(null, 0));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
