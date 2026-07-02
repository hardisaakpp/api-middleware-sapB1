using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Domain.Enums;

namespace Middleware.IntegrationTests.Controllers;

public sealed class DepositsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DepositsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISapGateway));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                var mock = new Mock<ISapGateway>();
                mock.Setup(g => g.CreateDepositAsync(
                        It.IsAny<CreateDepositRequest>(),
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DocumentCreatedResult(10, 200));

                services.AddScoped<ISapGateway>(_ => mock.Object);
            });
        });
    }

    [Fact]
    public async Task Post_Deposit_Returns201()
    {
        var client = _factory.CreateClient();

        var request = new CreateDepositRequest(
            Type: DepositType.MT,
            Environment: CompanyEnvironment.Test,
            DocDate: new DateOnly(2024, 6, 1),
            Comments: null,
            Lines: [new DepositLineDto("ITEM001", 5m, "WH01")]
        );

        var response = await client.PostAsJsonAsync("/api/v1/deposits", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Get_NonExistentEndpoint_Returns404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/deposits/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
