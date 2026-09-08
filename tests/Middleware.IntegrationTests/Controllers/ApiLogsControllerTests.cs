using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions;
using Middleware.IntegrationTests.Infrastructure;

namespace Middleware.IntegrationTests.Controllers;

/// <summary>
/// La bitácora es lo que soporte revisa cuando "la integración no pasó": el listado debe filtrar
/// por endpoint/errores y el detalle debe traer request y response tal como se registraron.
/// </summary>
public sealed class ApiLogsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly InMemoryApiLogRepository _repository = new();

    public ApiLogsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IApiLogRepository));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                services.AddScoped<IApiLogRepository>(_ => _repository);
            });
        });
    }

    private long Seed()
    {
        var baseDate = new DateTime(2026, 9, 7, 10, 0, 0);
        _repository.Seed(new ApiLogEntry("POST /api/v1/stock-transfers/direct", """{"fromWarehouse":"A01"}""", "", "alextoasa", 201, 850), baseDate);
        var failedId = _repository.Seed(new ApiLogEntry("POST /api/v1/liquidaciones", """{"idLiquidacion":3282}""", """{"title":"SAP error"}""", "system", 400, 1200), baseDate.AddMinutes(5));
        _repository.Seed(new ApiLogEntry("GET /api/v1/items/ABC", "", "", "system", 200, 90), baseDate.AddMinutes(10));
        return failedId;
    }

    [Fact]
    public async Task List_Default_IsNewestFirstWithoutBodies()
    {
        Seed();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/api-logs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("total").GetInt32().Should().Be(3);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.First().GetProperty("endpoint").GetString().Should().Be("GET /api/v1/items/ABC");
        // La API omite las propiedades null al serializar: "sin cuerpo" es ausente o null.
        items.Should().OnlyContain(i => IsNullOrMissing(i, "request") && IsNullOrMissing(i, "response"));
    }

    private static bool IsNullOrMissing(JsonElement item, string property) =>
        !item.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null;

    [Fact]
    public async Task List_OnlyErrorsAndEndpointFilter_ReturnMatches()
    {
        Seed();
        var client = _factory.CreateClient();

        var errors = await client.GetFromJsonAsync<JsonElement>("/api/v1/api-logs?onlyErrors=true");
        errors.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("statusCode").GetInt32().Should().Be(400);

        var byEndpoint = await client.GetFromJsonAsync<JsonElement>("/api/v1/api-logs?endpoint=stock-transfers");
        byEndpoint.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Get_ById_IncludesRequestAndResponse()
    {
        var failedId = Seed();
        var client = _factory.CreateClient();

        var body = await client.GetFromJsonAsync<JsonElement>($"/api/v1/api-logs/{failedId}");

        body.GetProperty("request").GetString().Should().Contain("3282");
        body.GetProperty("response").GetString().Should().Contain("SAP error");
        body.GetProperty("usuario").GetString().Should().Be("system");
    }

    [Fact]
    public async Task Get_Unknown_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/api-logs/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
