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

    [Fact]
    public async Task ExecuteAsync_NoSourceHasMovements_ReturnsAllFalse()
    {
        SetupGateway(hasPurchaseOrders: false, hasInventoryGenEntries: false, hasInventoryTransferRequests: false);

        var result = await _sut.ExecuteAsync("R190.1012");

        result.HasMovements.Should().BeFalse();
        result.HasPurchaseOrders.Should().BeFalse();
        result.HasInventoryGenEntries.Should().BeFalse();
        result.HasInventoryTransferRequests.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task ExecuteAsync_AnySourceHasMovements_HasMovementsIsTrue(
        bool purchaseOrders, bool inventoryGenEntries, bool inventoryTransferRequests)
    {
        SetupGateway(purchaseOrders, inventoryGenEntries, inventoryTransferRequests);

        var result = await _sut.ExecuteAsync("R190.1012");

        result.HasMovements.Should().BeTrue();
        result.HasPurchaseOrders.Should().Be(purchaseOrders);
        result.HasInventoryGenEntries.Should().Be(inventoryGenEntries);
        result.HasInventoryTransferRequests.Should().Be(inventoryTransferRequests);
    }

    [Fact]
    public async Task ExecuteAsync_QueriesAllThreeSourcesForTheSameItemCode()
    {
        SetupGateway(false, false, false);

        await _sut.ExecuteAsync("R190.1012");

        _gatewayMock.Verify(g => g.HasPurchaseOrderLineForItemAsync("R190.1012", It.IsAny<CancellationToken>()), Times.Once);
        _gatewayMock.Verify(g => g.HasInventoryGenEntryLineForItemAsync("R190.1012", It.IsAny<CancellationToken>()), Times.Once);
        _gatewayMock.Verify(g => g.HasInventoryTransferRequestLineForItemAsync("R190.1012", It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupGateway(bool hasPurchaseOrders, bool hasInventoryGenEntries, bool hasInventoryTransferRequests)
    {
        _gatewayMock
            .Setup(g => g.HasPurchaseOrderLineForItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasPurchaseOrders);
        _gatewayMock
            .Setup(g => g.HasInventoryGenEntryLineForItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasInventoryGenEntries);
        _gatewayMock
            .Setup(g => g.HasInventoryTransferRequestLineForItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasInventoryTransferRequests);
    }
}
