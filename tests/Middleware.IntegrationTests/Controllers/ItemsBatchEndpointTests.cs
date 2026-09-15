using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;
using Middleware.Infrastructure.Sap;
using Middleware.IntegrationTests.Infrastructure;

namespace Middleware.IntegrationTests.Controllers;

public sealed class ItemsBatchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public ItemsBatchEndpointTests(WebApplicationFactory<Program> factory)
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

    // For the queued path: swaps in an in-memory job repository and shortens the queue/backoff
    // timers so the transient-failure test does not depend on SQL or wall-clock delays.
    private HttpClient CreateQueueClient(Mock<ISapGateway> gatewayMock, InMemoryJobQueueRepository jobRepository)
    {
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JobQueue:QueueAfterSeconds"] = "1",
                    ["JobQueue:RetryBackoffSeconds:0"] = "1"
                });
            });

            builder.ConfigureServices(services =>
            {
                var gatewayDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISapGateway));
                if (gatewayDescriptor is not null)
                {
                    services.Remove(gatewayDescriptor);
                }
                services.AddScoped<ISapGateway>(_ => gatewayMock.Object);

                var repoDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IJobQueueRepository));
                if (repoDescriptor is not null)
                {
                    services.Remove(repoDescriptor);
                }
                services.AddSingleton<IJobQueueRepository>(jobRepository);
            });
        });
        return factory.CreateClient();
    }

    private static ItemResponse Item(string code) =>
        new(code, $"Nombre {code}", InventoryUoM: null, OnHand: 179m,
            PurchaseItem: true, SalesItem: true, InventoryItem: true, Valid: true, Frozen: false,
            ItemType: "itItems", PackCode: "X3", GroupCode: 103, GroupName: "R-MEDIAS",
            PreferredVendorCode: "P001", Pvp: null, CodeBars: null, Marca: null);

    [Fact]
    public async Task Post_MixOfExistingAndMissing_ReturnsItemsAndNotFound()
    {
        var client = CreateClient(mock =>
            mock.Setup(g => g.GetItemsByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<string> chunk, CancellationToken _) =>
                    chunk.Where(c => !c.StartsWith("NO-")).Select(Item).ToList()));

        var response = await client.PostAsJsonAsync("/api/v1/items/batch",
            new { itemCodes = new[] { "R215.810.NO", "780-2404174", "NO-EXISTE-123" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("groupName").GetString().Should().Be("R-MEDIAS");
        var notFound = body.RootElement.GetProperty("notFound");
        notFound.GetArrayLength().Should().Be(1);
        notFound[0].GetString().Should().Be("NO-EXISTE-123");
    }

    [Fact]
    public async Task Post_EmptyList_Returns200WithEmptyArrays()
    {
        var client = CreateClient(_ => { });

        var response = await client.PostAsJsonAsync("/api/v1/items/batch",
            new { itemCodes = Array.Empty<string>() });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        body.RootElement.GetProperty("notFound").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Post_MoreThan100Codes_Returns400()
    {
        var client = CreateClient(_ => { });

        var codes = Enumerable.Range(1, 101).Select(i => $"ITEM{i:D3}").ToArray();
        var response = await client.PostAsJsonAsync("/api/v1/items/batch", new { itemCodes = codes });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_TransientSapFailure_ReturnsQueued_ThenCompletesAfterRetry()
    {
        // The batch endpoint follows the job-queue pattern: a transient SAP failure (saturation)
        // no longer surfaces as an immediate 503 — it is queued (202 + jobId) and retried in the
        // background until it succeeds, so the caller can poll instead of losing the request.
        var callCount = 0;
        var gatewayMock = new Mock<ISapGateway>();
        gatewayMock
            .Setup(g => g.GetItemsByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<string> chunk, CancellationToken _) =>
            {
                callCount++;
                return callCount == 1
                    ? Task.FromException<IReadOnlyList<ItemResponse>>(new SapThrottleException("SAP throttle wait exhausted"))
                    : Task.FromResult<IReadOnlyList<ItemResponse>>(chunk.Select(Item).ToList());
            });

        var jobRepository = new InMemoryJobQueueRepository();
        var client = CreateQueueClient(gatewayMock, jobRepository);

        var response = await client.PostAsJsonAsync("/api/v1/items/batch",
            new { itemCodes = new[] { "A", "B" } });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var acceptedDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        acceptedDoc.RootElement.GetProperty("status").GetString().Should().Be("ENCOLADO");
        var jobId = acceptedDoc.RootElement.GetProperty("jobId").GetGuid();

        var job = await PollUntilTerminalAsync(client, jobId, TimeSpan.FromSeconds(10));

        job.GetProperty("status").GetString().Should().Be(
            "COMPLETADO", "a transient/saturation failure must be retried, not surfaced as a permanent error");
        job.GetProperty("httpStatusCode").GetInt32().Should().Be(200);
        job.GetProperty("result").GetProperty("items").GetArrayLength().Should().Be(2);
        callCount.Should().BeGreaterThanOrEqualTo(2);
    }

    private static async Task<JsonElement> PollUntilTerminalAsync(HttpClient client, Guid jobId, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            var response = await client.GetAsync($"/api/v1/jobs/{jobId}", cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            var doc = JsonDocument.Parse(body);
            var status = doc.RootElement.GetProperty("status").GetString();

            if (status is "COMPLETADO" or "ERROR")
            {
                return doc.RootElement;
            }

            await Task.Delay(200, cts.Token);
        }
    }
}
