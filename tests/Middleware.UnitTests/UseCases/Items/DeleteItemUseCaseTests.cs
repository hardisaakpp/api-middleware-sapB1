using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.UseCases.Items;

namespace Middleware.UnitTests.UseCases.Items;

public sealed class DeleteItemUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly DeleteItemUseCase _sut;

    public DeleteItemUseCaseTests()
    {
        _sut = new DeleteItemUseCase(_gatewayMock.Object, NullLogger<DeleteItemUseCase>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_DelegatesToGateway_AndReturnsItemCode()
    {
        _gatewayMock
            .Setup(g => g.DeleteItemAsync("R190.1012", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.ExecuteAsync("R190.1012");

        result.ItemCode.Should().Be("R190.1012");
        _gatewayMock.Verify(g => g.DeleteItemAsync("R190.1012", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_GatewayThrows_PropagatesException()
    {
        // SAP-specific error handling (404, or 400 when SAP refuses to delete an item with
        // transactions) lives in the controller and is covered by ItemsWriteEndpointTests;
        // this only asserts the use case doesn't swallow it.
        _gatewayMock
            .Setup(g => g.DeleteItemAsync("R190.1012", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SAP unavailable"));

        var act = () => _sut.ExecuteAsync("R190.1012");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
