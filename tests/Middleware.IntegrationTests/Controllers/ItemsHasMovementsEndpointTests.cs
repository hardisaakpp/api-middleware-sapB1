using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Middleware.Application.Dtos.Responses;
using Middleware.Infrastructure.Sap;

namespace Middleware.IntegrationTests.Controllers;

/// <summary>
/// Exercises the atomic Items lookup through the real SapGateway while replacing only the
/// low-level Service Layer client with a scripted fake.
/// </summary>
public sealed class ItemsHasMovementsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ExpectedResource =
        "Items('CS00003-11')?$select=ItemCode,QuantityOnStock," +
        "QuantityOrderedFromVendors,QuantityOrderedByCustomers";

    private readonly WebApplicationFactory<Program> _baseFactory;

    public ItemsHasMovementsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    private HttpClient CreateClient(ScriptedSapClient fakeClient)
    {
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // These endpoint tests do not exercise SAP warmup, SQL recovery, or audit workers.
                // Removing hosted services keeps startup fully isolated from external systems.
                foreach (var descriptor in services
                    .Where(d => d.ServiceType == typeof(IHostedService))
                    .ToList())
                {
                    services.Remove(descriptor);
                }

                var clientDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(ISapServiceLayerClient));
                if (clientDescriptor is not null)
                {
                    services.Remove(clientDescriptor);
                }
                services.AddSingleton<ISapServiceLayerClient>(_ => fakeClient);

                var sessionDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(ISapSessionManager));
                if (sessionDescriptor is not null)
                {
                    services.Remove(sessionDescriptor);
                }
                services.AddSingleton<ISapSessionManager>(_ => new MovementsStubSessionManager());
            });
        });
        return factory.CreateClient();
    }

    [Fact]
    public async Task Get_AllBalancesZero_ReturnsFalseFromSingleItemsCall()
    {
        const string json = """
            {
                "ItemCode": "CS00003-11",
                "QuantityOnStock": 0,
                "QuantityOrderedFromVendors": 0,
                "QuantityOrderedByCustomers": 0
            }
            """;
        var fake = new ScriptedSapClient(_ => json);
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/CS00003-11/has-movements");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HasItemMovementsResponse>();
        body!.ItemCode.Should().Be("CS00003-11");
        body.HasMovements.Should().BeFalse();
        fake.Resources.Should().ContainSingle().Which.Should().Be(ExpectedResource);
    }

    [Fact]
    public async Task Get_PositiveQuantityOnStock_ReturnsTrue()
    {
        const string json = """
            {
                "ItemCode": "CS00003-11",
                "QuantityOnStock": 5,
                "QuantityOrderedFromVendors": 0,
                "QuantityOrderedByCustomers": 0
            }
            """;
        var fake = new ScriptedSapClient(_ => json);
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/CS00003-11/has-movements");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HasItemMovementsResponse>();
        body!.HasMovements.Should().BeTrue();
        fake.Resources.Should().ContainSingle().Which.Should().Be(ExpectedResource);
    }

    [Fact]
    public async Task Get_ItemNotFoundInSap_ReturnsFalse()
    {
        var fake = new ScriptedSapClient(_ =>
            throw new SapException(HttpStatusCode.NotFound, "Item not found"));
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/CS00003-11/has-movements");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HasItemMovementsResponse>();
        body!.HasMovements.Should().BeFalse();
        fake.Resources.Should().ContainSingle().Which.Should().Be(ExpectedResource);
    }

    [Fact]
    public async Task Get_ServiceLayerConnectionFailure_Returns5xx()
    {
        var fake = new ScriptedSapClient(_ =>
            throw new HttpRequestException("Service Layer unreachable"));
        var client = CreateClient(fake);

        var response = await client.GetAsync("/api/v1/items/CS00003-11/has-movements");

        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(500);
    }

    private sealed class ScriptedSapClient : ISapServiceLayerClient
    {
        private readonly Func<string, string> _responder;
        private readonly List<string> _resources = [];

        public ScriptedSapClient(Func<string, string> responder) => _responder = responder;

        public IReadOnlyList<string> Resources
        {
            get { lock (_resources) { return _resources.ToList(); } }
        }

        public Task<string> SendAsync(
            HttpMethod method,
            string relativeUrl,
            string? body,
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            lock (_resources) { _resources.Add(relativeUrl); }
            return Task.FromResult(_responder(relativeUrl));
        }
    }
}

file sealed class MovementsStubSessionManager : ISapSessionManager
{
    public Task<string> GetSessionTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult("fake-session-token");

    public Task<string> GetSessionTokenAsync(
        string companyDb, string password, CancellationToken cancellationToken = default)
        => Task.FromResult("fake-session-token");

    public Task InvalidateSessionAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InvalidateSessionAsync(string companyDb, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
