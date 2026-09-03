using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Infrastructure.Sap;

namespace Middleware.IntegrationTests.Controllers;

/// <summary>
/// Covers POST/PATCH /api/v1/items — in particular the SAP-error passthrough that Create/Update
/// give Items but no other resource: a SapException must surface with SAP's original status code
/// (e.g. 400/-2035 duplicate ItemCode) instead of the generic 502 GlobalExceptionMiddleware applies
/// everywhere else, since api-articulo-maestro's proactive-upsert flow depends on seeing that 400.
/// </summary>
public sealed class ItemsWriteEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public ItemsWriteEndpointTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    private HttpClient CreateClient(Action<Mock<ISapGateway>> configureMock)
    {
        var mock = new Mock<ISapGateway>();
        configureMock(mock);

        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISapGateway));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }
                services.AddScoped<ISapGateway>(_ => mock.Object);
            });
        });

        return factory.CreateClient();
    }

    [Fact]
    public async Task Post_Item_Returns201_OnSuccess()
    {
        var client = CreateClient(mock => mock
            .Setup(g => g.CreateItemAsync(It.IsAny<CreateItemRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask));

        var request = new CreateItemRequest(ItemCode: "R190.1012", ItemName: "CASUAL LISA X1");
        var response = await client.PostAsJsonAsync("/api/v1/items", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Post_Item_MissingItemName_Returns400ValidationProblem()
    {
        var client = CreateClient(_ => { });

        var response = await client.PostAsJsonAsync("/api/v1/items", new { ItemCode = "R190.1012" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_Item_SapRejectsDuplicateItemCode_Returns400WithOriginalSapBody()
    {
        const string sapErrorBody = """{"error":{"code":-2035,"message":{"value":"Item already exists in @OITM"}}}""";

        var client = CreateClient(mock => mock
            .Setup(g => g.CreateItemAsync(It.IsAny<CreateItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SapException(HttpStatusCode.BadRequest, sapErrorBody)));

        var request = new CreateItemRequest(ItemCode: "R190.1012", ItemName: "CASUAL LISA X1");
        var response = await client.PostAsJsonAsync("/api/v1/items", request);

        // Everywhere else in this API a SapException collapses to 502 (GlobalExceptionMiddleware).
        // Items must instead preserve SAP's real 400 so api-articulo-maestro's proactive-upsert can detect -2035.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("-2035");
    }

    [Fact]
    public async Task Patch_Item_Returns200_OnSuccess()
    {
        var client = CreateClient(mock => mock
            .Setup(g => g.UpdateItemAsync("R190.1012", It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask));

        var response = await client.PatchAsync("/api/v1/items/R190.1012",
            JsonContent.Create(new UpdateItemRequest(U_IXX_TALLA: "XL")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Patch_Item_NotFoundInSap_Returns404()
    {
        var client = CreateClient(mock => mock
            .Setup(g => g.UpdateItemAsync("UNKNOWN", It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SapException(HttpStatusCode.NotFound, "not found")));

        var response = await client.PatchAsync("/api/v1/items/UNKNOWN",
            JsonContent.Create(new UpdateItemRequest(U_IXX_TALLA: "XL")));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Item_Returns204_OnSuccess()
    {
        var client = CreateClient(mock => mock
            .Setup(g => g.DeleteItemAsync("R190.1012", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask));

        var response = await client.DeleteAsync("/api/v1/items/R190.1012");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_Item_NotFoundInSap_Returns404()
    {
        var client = CreateClient(mock => mock
            .Setup(g => g.DeleteItemAsync("UNKNOWN", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SapException(HttpStatusCode.NotFound, "not found")));

        var response = await client.DeleteAsync("/api/v1/items/UNKNOWN");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Item_SapRefusesBecauseOfTransactions_ReturnsOriginalSapStatusAndBody()
    {
        const string sapErrorBody = """{"error":{"code":-5002,"message":{"value":"Cannot delete or update; transactions exist"}}}""";

        var client = CreateClient(mock => mock
            .Setup(g => g.DeleteItemAsync("R190.1012", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SapException(HttpStatusCode.BadRequest, sapErrorBody)));

        var response = await client.DeleteAsync("/api/v1/items/R190.1012");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("-5002");
    }
}
