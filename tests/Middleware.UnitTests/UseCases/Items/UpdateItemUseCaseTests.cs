using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.Items;

namespace Middleware.UnitTests.UseCases.Items;

public sealed class UpdateItemUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly UpdateItemUseCase _sut;

    public UpdateItemUseCaseTests()
    {
        _sut = new UpdateItemUseCase(_gatewayMock.Object, NullLogger<UpdateItemUseCase>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_DelegatesToGateway_AndReturnsSuccess()
    {
        var request = new UpdateItemRequest(ItemCode: "R190.1012", U_IXX_TALLA: "XL");

        _gatewayMock
            .Setup(g => g.UpdateItemAsync("R190.1012", request, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.ExecuteAsync(request);

        result.Success.Should().BeTrue();
        result.ItemCode.Should().Be("R190.1012");
        _gatewayMock.Verify(g => g.UpdateItemAsync("R190.1012", request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_GatewayThrows_PropagatesException()
    {
        // SAP-specific error handling (404/400 passthrough) lives in the controller and is
        // covered by ItemsWriteEndpointTests; this only asserts the use case doesn't swallow it.
        var request = new UpdateItemRequest(ItemCode: "UNKNOWN");

        _gatewayMock
            .Setup(g => g.UpdateItemAsync("UNKNOWN", request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("not found"));

        var act = () => _sut.ExecuteAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
