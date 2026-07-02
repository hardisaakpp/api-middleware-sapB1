using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.PurchaseOrders;

namespace Middleware.UnitTests.UseCases.PurchaseOrders;

public sealed class GetPurchaseOrderUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly GetPurchaseOrderUseCase _sut;

    public GetPurchaseOrderUseCaseTests()
    {
        _sut = new GetPurchaseOrderUseCase(_gatewayMock.Object, NullLogger<GetPurchaseOrderUseCase>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_ExistingDocEntry_ReturnsPurchaseOrder()
    {
        var expected = new PurchaseOrderResponse(
            DocEntry: 99,
            DocNum: 5000,
            VendorCode: "V001",
            DocDate: "2024-06-01",
            DocStatus: "bost_Open",
            Lines: [new PurchaseOrderLineResponse(0, "ITEM001", 10m, 10m, 100m, "WH01")]
        );

        _gatewayMock
            .Setup(g => g.GetPurchaseOrderAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _sut.ExecuteAsync(new GetPurchaseOrderRequest(99));

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task ExecuteAsync_GatewayThrows_PropagatesException()
    {
        _gatewayMock
            .Setup(g => g.GetPurchaseOrderAsync(1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Not found"));

        var act = () => _sut.ExecuteAsync(new GetPurchaseOrderRequest(1));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
