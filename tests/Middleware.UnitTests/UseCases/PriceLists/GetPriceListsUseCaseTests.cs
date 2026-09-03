using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.PriceLists;

namespace Middleware.UnitTests.UseCases.PriceLists;

public sealed class GetPriceListsUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly GetPriceListsUseCase _sut;

    public GetPriceListsUseCaseTests()
    {
        _sut = new GetPriceListsUseCase(_gatewayMock.Object, NullLogger<GetPriceListsUseCase>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCatalogFromGateway()
    {
        _gatewayMock
            .Setup(g => g.GetPriceListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PriceListResponse(1, "Lista Minorista"),
                new PriceListResponse(2, "Lista Internacional")
            ]);

        var result = await _sut.ExecuteAsync();

        result.PriceLists.Should().HaveCount(2);
        result.PriceLists[0].PriceListName.Should().Be("Lista Minorista");
    }
}
