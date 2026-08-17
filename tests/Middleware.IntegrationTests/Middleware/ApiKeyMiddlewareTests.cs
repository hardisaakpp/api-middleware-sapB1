using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Middleware.IntegrationTests.Middleware;

/// <summary>
/// The Development environment (used by every other integration test in this suite via the
/// default WebApplicationFactory) skips ApiKey enforcement when no key is configured, so those
/// tests keep working unmodified. This file exercises the middleware itself with an explicitly
/// configured key, including the /health exemption that keeps liveness/readiness probes working
/// once the key is enforced.
/// </summary>
public sealed class ApiKeyMiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ConfiguredKey = "test-shared-secret";

    private readonly WebApplicationFactory<Program> _baseFactory;

    public ApiKeyMiddlewareTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    private HttpClient CreateClientWithConfiguredKey()
    {
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiKey:Value"] = ConfiguredKey
                });
            });
        });
        return factory.CreateClient();
    }

    [Fact]
    public async Task Request_WithoutHeader_WhenKeyConfigured_Returns401()
    {
        var client = CreateClientWithConfiguredKey();

        var response = await client.GetAsync("/api/v1/warehouses");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithWrongKey_Returns401()
    {
        var client = CreateClientWithConfiguredKey();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        var response = await client.GetAsync("/api/v1/warehouses");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_WhenKeyConfigured_DoesNotRequireApiKey()
    {
        var client = CreateClientWithConfiguredKey();

        var response = await client.GetAsync("/health");

        // /health's own 200/503 depends on the real SapHealthCheck reaching SAP — irrelevant here.
        // What this test verifies is that ApiKeyMiddleware never got the chance to reject the
        // request for lack of a key: a 401 would mean the /health exemption regressed.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithoutHeader_WhenNoKeyConfigured_IsAllowedInDevelopment()
    {
        // Default factory: Development environment, no ApiKey:Value configured — matches every
        // other integration test in this suite, which send no header at all. A 401 here would mean
        // every other test in the suite is silently relying on Development-only auth bypass drifting.
        var client = _baseFactory.CreateClient();

        var response = await client.GetAsync("/api/v1/warehouses");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
