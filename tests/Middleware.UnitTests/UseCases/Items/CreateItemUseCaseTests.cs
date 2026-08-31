using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.Items;

namespace Middleware.UnitTests.UseCases.Items;

public sealed class CreateItemUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly CreateItemUseCase _sut;

    public CreateItemUseCaseTests()
    {
        _sut = new CreateItemUseCase(_gatewayMock.Object, NullLogger<CreateItemUseCase>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_DelegatesToGateway_AndReturnsItemCode()
    {
        var request = new CreateItemRequest(ItemCode: "R190.1012", ItemName: "CASUAL LISA X1");

        _gatewayMock
            .Setup(g => g.CreateItemAsync(request, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.ExecuteAsync(request);

        result.ItemCode.Should().Be("R190.1012");
        _gatewayMock.Verify(g => g.CreateItemAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_GatewayThrows_PropagatesException()
    {
        // SAP-specific error handling (status passthrough for -2035 etc.) lives in the controller
        // and is covered by ItemsWriteEndpointTests; this only asserts the use case doesn't swallow it.
        var request = new CreateItemRequest(ItemCode: "R190.1012", ItemName: "CASUAL LISA X1");

        _gatewayMock
            .Setup(g => g.CreateItemAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SAP unavailable"));

        var act = () => _sut.ExecuteAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
