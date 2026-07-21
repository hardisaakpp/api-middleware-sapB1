using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions;
using Middleware.IntegrationTests.Infrastructure;
using Moq;

namespace Middleware.IntegrationTests.JobQueue;

public sealed class JobQueueTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public JobQueueTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateFactory(Mock<ISapGateway> gatewayMock, InMemoryJobQueueRepository jobRepository)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
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
    }

    [Fact]
    public async Task FastSapResponse_ReturnsInline_NoJobQueued()
    {
        var gatewayMock = new Mock<ISapGateway>();
        gatewayMock
            .Setup(g => g.UpdateInventoryTransferRequestStatusAsync(396536, "1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jobRepository = new InMemoryJobQueueRepository();
        var client = CreateFactory(gatewayMock, jobRepository).CreateClient();

        var response = await client.PatchAsync(
            "/api/v1/inventory-transfer-requests/396536/status",
            JsonContent.Create(new { U_STATUS = "1" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("docEntry").GetInt32().Should().Be(396536);

        jobRepository.Count.Should().Be(0, "a fast response should never touch the durable queue");
    }

    [Fact]
    public async Task SlowSapResponse_ReturnsQueued_ThenCompletesAsynchronously()
    {
        var gatewayMock = new Mock<ISapGateway>();
        gatewayMock
            .Setup(g => g.UpdateInventoryTransferRequestStatusAsync(396537, "1", It.IsAny<CancellationToken>()))
            .Returns(() => Task.Delay(TimeSpan.FromSeconds(3)));

        var jobRepository = new InMemoryJobQueueRepository();
        var client = CreateFactory(gatewayMock, jobRepository).CreateClient();

        var response = await client.PatchAsync(
            "/api/v1/inventory-transfer-requests/396537/status",
            JsonContent.Create(new { U_STATUS = "1" }));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var acceptedDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        acceptedDoc.RootElement.GetProperty("status").GetString().Should().Be("ENCOLADO");
        var jobId = acceptedDoc.RootElement.GetProperty("jobId").GetGuid();

        var job = await PollUntilTerminalAsync(client, jobId, TimeSpan.FromSeconds(10));

        job.GetProperty("status").GetString().Should().Be("COMPLETADO");
        job.GetProperty("httpStatusCode").GetInt32().Should().Be(200);
        job.GetProperty("result").GetProperty("docEntry").GetInt32().Should().Be(396537);
    }

    [Fact]
    public async Task TransientFailure_IsRetried_UntilItSucceeds()
    {
        var callCount = 0;
        var gatewayMock = new Mock<ISapGateway>();
        gatewayMock
            .Setup(g => g.UpdateInventoryTransferRequestStatusAsync(396538, "2", It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                return callCount == 1
                    ? Task.FromException(new HttpRequestException("simulated SAP saturation"))
                    : Task.CompletedTask;
            });

        var jobRepository = new InMemoryJobQueueRepository();
        var client = CreateFactory(gatewayMock, jobRepository).CreateClient();

        var response = await client.PatchAsync(
            "/api/v1/inventory-transfer-requests/396538/status",
            JsonContent.Create(new { U_STATUS = "2" }));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var acceptedDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var jobId = acceptedDoc.RootElement.GetProperty("jobId").GetGuid();

        var job = await PollUntilTerminalAsync(client, jobId, TimeSpan.FromSeconds(10));

        job.GetProperty("status").GetString().Should().Be(
            "COMPLETADO", "a transient/saturation failure must be retried, not surfaced as a permanent error");
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
