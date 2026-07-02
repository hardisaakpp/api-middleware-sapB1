using System.Net;
using FluentAssertions;
using Middleware.IntegrationTests.Infrastructure;

namespace Middleware.IntegrationTests.Concurrency;

/// <summary>
/// Concurrency tests for the SAP throttle layer.
/// All tests run against a fake ISapServiceLayerClient — no real SAP calls are made.
/// SapGateway and SapThrottle are the real production implementations.
/// </summary>
public sealed class SapThrottleTests : IAsyncLifetime
{
    // 150 ms delay per fake SAP call keeps tests fast (~20 reqs / 2 slots = ~1.5 s total)
    private readonly SapConcurrencyFactory _factory = new(sapCallDelay: TimeSpan.FromMilliseconds(150));

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Throttle_CapsSimultaneousSapCalls_AtConfiguredLimit()
    {
        const int concurrentRequests = 20;
        const int configuredLimit = 2; // matches Sap:MaxConcurrentSapCalls override in the factory

        var client = _factory.CreateClient();

        // Fire all requests at once — each hits a real SapGateway → real SapThrottle → fake HTTP client
        var tasks = Enumerable.Range(1, concurrentRequests)
            .Select(i => client.GetAsync($"/api/v1/purchase-orders/{i}"))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        // Every request must succeed
        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().Be(HttpStatusCode.OK,
                because: "the fake client always returns a valid response"));

        // The throttle must have prevented more than 2 concurrent SAP calls
        _factory.FakeClient.PeakConcurrentCalls.Should().BeLessThanOrEqualTo(configuredLimit,
            because: $"SapThrottle is configured with MaxConcurrentSapCalls={configuredLimit}");

        // Sanity: every request reached SAP exactly once
        _factory.FakeClient.TotalCalls.Should().Be(concurrentRequests);
    }

    [Fact]
    public async Task Throttle_DoesNotDropRequests_UnderHighConcurrency()
    {
        const int concurrentRequests = 50;

        var client = _factory.CreateClient();

        var tasks = Enumerable.Range(1, concurrentRequests)
            .Select(i => client.GetAsync($"/api/v1/purchase-orders/{i}"))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().Be(HttpStatusCode.OK));

        _factory.FakeClient.TotalCalls.Should().Be(concurrentRequests,
            because: "every request must reach SAP, not be dropped or short-circuited");
    }
}
