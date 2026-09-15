using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.UseCases.Items;

namespace Middleware.UnitTests.UseCases.Items;

public sealed class HasItemMovementsUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly HasItemMovementsUseCase _sut;

    public HasItemMovementsUseCaseTests()
    {
        _sut = new HasItemMovementsUseCase(_gatewayMock.Object, NullLogger<HasItemMovementsUseCase>.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ReturnsAtomicGatewayResult(bool hasMovements)
    {
        _gatewayMock
            .Setup(g => g.HasItemMovementsAsync("CS00003-11", It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasMovements);

        var result = await _sut.ExecuteAsync("CS00003-11");

        result.ItemCode.Should().Be("CS00003-11");
        result.HasMovements.Should().Be(hasMovements);
        _gatewayMock.Verify(
            g => g.HasItemMovementsAsync("CS00003-11", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
