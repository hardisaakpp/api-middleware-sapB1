using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.StockTransfer;

namespace Middleware.UnitTests.UseCases.StockTransfer;

public sealed class CreateStockTransferTestUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly CreateStockTransferTestUseCase _sut;

    public CreateStockTransferTestUseCaseTests()
    {
        _sut = new CreateStockTransferTestUseCase(_gatewayMock.Object, NullLogger<CreateStockTransferTestUseCase>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_ReturnsDocumentNumbers()
    {
        var request = new CreateStockTransferRequest(
            FromWarehouse: "WH01",
            ToWarehouse: "WH02",
            DocDate: new DateOnly(2024, 6, 1),
            Comments: null,
            Lines: [new StockTransferLineDto("ITEM001", 10m)]
        );

        _gatewayMock
            .Setup(g => g.CreateStockTransferTestAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentCreatedResult(42, 1001));

        var result = await _sut.ExecuteAsync(request);

        result.DocEntry.Should().Be(42);
        result.DocNum.Should().Be(1001);
        _gatewayMock.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_GatewayThrows_PropagatesException()
    {
        var request = new CreateStockTransferRequest(
            FromWarehouse: "WH01",
            ToWarehouse: "WH02",
            DocDate: new DateOnly(2024, 6, 1),
            Comments: null,
            Lines: [new StockTransferLineDto("ITEM001", 5m)]
        );

        _gatewayMock
            .Setup(g => g.CreateStockTransferTestAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SAP unavailable"));

        var act = () => _sut.ExecuteAsync(request);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("SAP unavailable");
    }
}
