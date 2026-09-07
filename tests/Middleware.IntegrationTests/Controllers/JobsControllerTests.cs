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
/// GET /jobs es la vista de soporte de la cola SAP en Nexus: tiene que poder filtrar por estado y
/// endpoint sin arrastrar payloads, y el detalle por id sí debe traer lo que se envió a SAP.
/// </summary>
public sealed class JobsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly InMemoryJobQueueRepository _repository = new();

    public JobsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IJobQueueRepository));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                services.AddScoped<IJobQueueRepository>(_ => _repository);
            });
        });
    }

    private async Task<(Guid Completed, Guid Failed, Guid Queued)> SeedAsync()
    {
        var completed = Guid.NewGuid();
        await _repository.InsertQueuedAsync(completed, "POST /api/v1/stock-transfers/direct", "CreateStockTransferDirect", """{"fromWarehouse":"A01"}""");
        await _repository.MarkCompletedAsync(completed, 201, """{"docEntry":45,"docNum":1004}""");

        var failed = Guid.NewGuid();
        await _repository.InsertQueuedAsync(failed, "POST /api/v1/liquidaciones", "CreateLiquidacion", """{"idLiquidacion":3282}""");
        await _repository.MarkErrorAsync(failed, "SAP saturado: se agotaron los reintentos.", 503);

        var queued = Guid.NewGuid();
        await _repository.InsertQueuedAsync(queued, "POST /api/v1/goods-receipts", "CreateGoodsReceipt", """{"docEntry":1}""");

        return (completed, failed, queued);
    }

    [Fact]
    public async Task List_WithoutFilters_ReturnsAllJobsWithoutPayloads()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/jobs?pageSize=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("total").GetInt32().Should().Be(3);
        body.GetProperty("totalPages").GetInt32().Should().Be(1);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(3);
        // La API omite las propiedades null al serializar: "sin payload" es ausente o null.
        items.Should().OnlyContain(i => IsNullOrMissing(i, "result") && IsNullOrMissing(i, "request"));
    }

    private static bool IsNullOrMissing(JsonElement item, string property) =>
        !item.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null;

    [Fact]
    public async Task List_FilterByStatusAndEndpoint_ReturnsOnlyMatches()
    {
        var (_, failed, _) = await SeedAsync();
        var client = _factory.CreateClient();

        var byStatus = await client.GetFromJsonAsync<JsonElement>("/api/v1/jobs?status=error");
        byStatus.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("jobId").GetGuid().Should().Be(failed);

        var byEndpoint = await client.GetFromJsonAsync<JsonElement>("/api/v1/jobs?endpoint=stock-transfers");
        byEndpoint.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("status").GetString().Should().Be("COMPLETADO");
    }

    [Fact]
    public async Task List_InvalidStatus_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/jobs?status=PENDIENTE");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_ById_IncludesRequestAndResult()
    {
        var (completed, _, _) = await SeedAsync();
        var client = _factory.CreateClient();

        var body = await client.GetFromJsonAsync<JsonElement>($"/api/v1/jobs/{completed}");

        body.GetProperty("requestType").GetString().Should().Be("CreateStockTransferDirect");
        body.GetProperty("request").GetProperty("fromWarehouse").GetString().Should().Be("A01");
        body.GetProperty("result").GetProperty("docNum").GetInt32().Should().Be(1004);
        body.GetProperty("httpStatusCode").GetInt32().Should().Be(201);
    }
}
