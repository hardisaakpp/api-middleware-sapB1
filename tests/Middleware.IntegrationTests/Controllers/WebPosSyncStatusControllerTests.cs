using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;
using Middleware.IntegrationTests.Infrastructure;

namespace Middleware.IntegrationTests.Controllers;

/// <summary>
/// The sync-status endpoints are what Nexus uses to tell the user whether a document reached
/// HITELL and, when it didn't, whether the fix is "create the product in HITELL" — so the
/// interpretation (result / rejection) must come ready-made from the API.
/// </summary>
public sealed class WebPosSyncStatusControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly InMemoryPosSyncStatusRepository _repository = new();

    public WebPosSyncStatusControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPosSyncStatusRepository));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                services.AddScoped<IPosSyncStatusRepository>(_ => _repository);
            });
        });
    }

    private async Task SeedAsync()
    {
        await _repository.UpsertAsync("ST", 587061, 1061, "SYNCED", null);
        await _repository.UpsertAsync("ST", 587065, 1065, "REJECTED", "LIMP0027 (1): No entity found for query");
        await _repository.UpsertAsync("GR", 900, 5900, "REJECTED", "SKBLZ0147 (1): El producto no existe.");
        await _repository.UpsertAsync("ST", 587070, 1070, "REJECTED", "ACHOME001 (1): La tienda no pertenece al negocio");
        await _repository.UpsertAsync("ST", 587080, 1080, "SKIPPED_NOT_POS", null);
    }

    [Fact]
    public async Task Get_RejectedForMissingProduct_ReturnsProductNotFoundRejectionWithRetryEndpoint()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/webpos/sync-status/st/587065");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WebPosSyncStatusResponse>();
        body!.Result.Should().Be("RECHAZADO");
        body.Status.Should().Be("REJECTED");
        body.DocNum.Should().Be(1065);
        body.Rejection.Should().NotBeNull();
        body.Rejection!.Type.Should().Be("PRODUCTO_NO_EXISTE");
        body.Rejection.Description.Should().Contain("LIMP0027");
        body.Rejection.Items.Should().ContainSingle(i => i.ItemCode == "LIMP0027" && i.CodBusiness == "1" && i.Type == "PRODUCTO_NO_EXISTE");
        body.Rejection.RetryEndpoint.Should().Be("/api/v1/webpos/stock-transfers/587065/retry");
    }

    [Fact]
    public async Task Get_SyncedAndSkipped_HaveBusinessResultAndNoRejection()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var synced = await client.GetFromJsonAsync<WebPosSyncStatusResponse>("/api/v1/webpos/sync-status/ST/587061");
        synced!.Result.Should().Be("INTEGRADO");
        synced.Rejection.Should().BeNull();

        var skipped = await client.GetFromJsonAsync<WebPosSyncStatusResponse>("/api/v1/webpos/sync-status/ST/587080");
        skipped!.Result.Should().Be("NO_CORRESPONDE");
        skipped.Rejection.Should().BeNull();
    }

    [Fact]
    public async Task Get_GoodsReceiptRejection_PointsToGoodsReceiptRetry()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var body = await client.GetFromJsonAsync<WebPosSyncStatusResponse>("/api/v1/webpos/sync-status/GR/900");

        body!.Rejection!.Type.Should().Be("PRODUCTO_NO_EXISTE");
        body.Rejection.RetryEndpoint.Should().Be("/api/v1/webpos/goods-receipts/900/retry");
    }

    [Fact]
    public async Task List_FilterByRejectionType_ReturnsOnlyMissingProductsWithPaging()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/webpos/sync-status?rejectionType=PRODUCTO_NO_EXISTE&pageSize=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ListResponse>();
        body!.Total.Should().Be(2);
        body.TotalPages.Should().Be(2);
        body.Entries.Should().ContainSingle()
            .Which.Should().Match<WebPosSyncStatusResponse>(e => e.Result == "RECHAZADO" && e.Rejection!.Type == "PRODUCTO_NO_EXISTE");
    }

    [Fact]
    public async Task List_FilterByDocNum_ReturnsThatDocument()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var body = await client.GetFromJsonAsync<ListResponse>("/api/v1/webpos/sync-status?docNum=5900");

        body!.Entries.Should().ContainSingle(e => e.DocType == "GR" && e.DocEntry == 900 && e.DocNum == 5900);
    }

    [Fact]
    public async Task List_DateRangeExcludingEverything_ReturnsEmptyPage()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var body = await client.GetFromJsonAsync<ListResponse>("/api/v1/webpos/sync-status?from=2000-01-01&to=2000-01-31");

        body!.Total.Should().Be(0);
        body.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task List_InvalidRejectionType_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/webpos/sync-status?rejectionType=LO_QUE_SEA");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record ListResponse(IReadOnlyList<WebPosSyncStatusResponse> Entries, int Total, int Page, int PageSize, int TotalPages);
}
