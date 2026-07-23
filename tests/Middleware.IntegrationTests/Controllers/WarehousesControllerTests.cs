using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.IntegrationTests.Controllers;

public sealed class WarehousesControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public WarehousesControllerTests(WebApplicationFactory<Program> factory)
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
    public async Task Get_ReturnsFullCatalog()
    {
        var client = CreateClient(mock =>
            mock.Setup(g => g.GetWarehousesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new WarehouseResponse("RL-CD", "BODEGA ROLAND CD"),
                    new WarehouseResponse("TIENDA01", "TIENDA 01 QUITO")
                ]));

        var response = await client.GetAsync("/api/v1/warehouses");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WarehousesListResponse>();
        body!.Warehouses.Should().HaveCount(2);
        body.Warehouses[0].WhsCode.Should().Be("RL-CD");
        body.Warehouses[0].WhsName.Should().Be("BODEGA ROLAND CD");
    }

    [Fact]
    public async Task Validate_PartialChunkFailure_Returns200WithConnectionErrorsOnlyForFailedChunk()
    {
        // Default chunk size is 20 → 25 codes produce 2 chunks; the second one (WH021+) fails
        var client = CreateClient(mock =>
        {
            mock.Setup(g => g.GetWarehousesByCodesAsync(
                    It.Is<IReadOnlyList<string>>(c => c.Contains("WH021")),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Service Layer unreachable"));
            mock.Setup(g => g.GetWarehousesByCodesAsync(
                    It.Is<IReadOnlyList<string>>(c => !c.Contains("WH021")),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<string> chunk, CancellationToken _) =>
                    chunk.Select(c => new WarehouseResponse(c, $"Bodega {c}")).ToList());
        });

        var codes = Enumerable.Range(1, 25).Select(i => $"WH{i:D3}").ToArray();
        var response = await client.PostAsJsonAsync("/api/v1/warehouses/validate", new { warehouseCodes = codes });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ValidateWarehousesResponse>();
        body!.Warehouses.Should().HaveCount(25);
        body.Warehouses.Where(w => int.Parse(w.WhsCode[2..]) <= 20)
            .Should().OnlyContain(w => w.Exists && w.ErrorType == null);
        body.Warehouses.Where(w => int.Parse(w.WhsCode[2..]) > 20)
            .Should().OnlyContain(w => !w.Exists && w.ErrorType == "connection_error");
    }

    [Fact]
    public async Task Validate_TotalServiceLayerFailure_Returns503()
    {
        var client = CreateClient(mock =>
            mock.Setup(g => g.GetWarehousesByCodesAsync(
                    It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Service Layer unreachable")));

        var response = await client.PostAsJsonAsync("/api/v1/warehouses/validate",
            new { warehouseCodes = new[] { "A", "B" } });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Validate_MoreThan200Codes_Returns400()
    {
        var client = CreateClient(_ => { });

        var codes = Enumerable.Range(1, 201).Select(i => $"WH{i:D3}").ToArray();
        var response = await client.PostAsJsonAsync("/api/v1/warehouses/validate", new { warehouseCodes = codes });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Validate_EmptyList_Returns400()
    {
        var client = CreateClient(_ => { });

        var response = await client.PostAsJsonAsync("/api/v1/warehouses/validate",
            new { warehouseCodes = Array.Empty<string>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
