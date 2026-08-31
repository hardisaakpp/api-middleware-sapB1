using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.StockTransfer;

namespace Middleware.UnitTests.UseCases.StockTransfer;

public sealed class CreateStockTransferBasedOnDocumentUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly CreateStockTransferBasedOnDocumentUseCase _sut;

    public CreateStockTransferBasedOnDocumentUseCaseTests()
    {
        _sut = new CreateStockTransferBasedOnDocumentUseCase(_gatewayMock.Object, NullLogger<CreateStockTransferBasedOnDocumentUseCase>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_ReturnsDocumentNumbers()
    {
        var request = new CreateStockTransferBasedOnDocumentRequest(
            DocObjectCode: null,
            CardCode: null,
            DocDate: "2024-06-01",
            TaxDate: null,
            Comments: null,
            FromWarehouse: "WH01",
            ToWarehouse: "WH02",
            U_ORIGEN: null,
            Series: null,
            PriceList: null,
            StockTransferLines: [new StockTransferBasedOnDocumentLineDto(17, 123, 0, "ITEM001", 10m, "WH02")]
        );

        _gatewayMock
            .Setup(g => g.CreateStockTransferBasedOnDocumentAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentCreatedResult(42, 1001));

        var result = await _sut.ExecuteAsync(request);

        result.DocEntry.Should().Be(42);
        result.DocNum.Should().Be(1001);
        _gatewayMock.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_GatewayThrows_PropagatesException()
    {
        var request = new CreateStockTransferBasedOnDocumentRequest(
            DocObjectCode: null,
            CardCode: null,
            DocDate: "2024-06-01",
            TaxDate: null,
            Comments: null,
            FromWarehouse: "WH01",
            ToWarehouse: "WH02",
            U_ORIGEN: null,
            Series: null,
            PriceList: null,
            StockTransferLines: [new StockTransferBasedOnDocumentLineDto(null, null, null, "ITEM001", 5m, "WH02")]
        );

        _gatewayMock
            .Setup(g => g.CreateStockTransferBasedOnDocumentAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SAP unavailable"));

        var act = () => _sut.ExecuteAsync(request);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("SAP unavailable");
    }
}
