using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.StockTransfer;

namespace Middleware.UnitTests.UseCases.StockTransfer;

public sealed class CreateStockTransferWithBinsUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly CreateStockTransferWithBinsUseCase _sut;

    public CreateStockTransferWithBinsUseCaseTests()
    {
        _sut = new CreateStockTransferWithBinsUseCase(_gatewayMock.Object, NullLogger<CreateStockTransferWithBinsUseCase>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_ReturnsDocumentNumbers()
    {
        var request = new CreateStockTransferWithBinsRequest(
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
            StockTransferLines:
            [
                new StockTransferWithBinsLineDto(
                    BaseType: null,
                    BaseEntry: null,
                    BaseLine: null,
                    ItemCode: "ITEM001",
                    Quantity: 10m,
                    WarehouseCode: "WH02",
                    StockTransferLinesBinAllocations:
                    [
                        new StockTransferBinAllocationDto(BinAbsEntry: 5, Quantity: 10m, BinActionType: 2),
                        new StockTransferBinAllocationDto(BinAbsEntry: 9, Quantity: 10m, BinActionType: 1)
                    ])
            ]
        );

        _gatewayMock
            .Setup(g => g.CreateStockTransferWithBinsAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentCreatedResult(42, 1001));

        var result = await _sut.ExecuteAsync(request);

        result.DocEntry.Should().Be(42);
        result.DocNum.Should().Be(1001);
        _gatewayMock.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_GatewayThrows_PropagatesException()
    {
        var request = new CreateStockTransferWithBinsRequest(
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
            StockTransferLines:
            [
                new StockTransferWithBinsLineDto(
                    BaseType: null,
                    BaseEntry: null,
                    BaseLine: null,
                    ItemCode: "ITEM001",
                    Quantity: 5m,
                    WarehouseCode: "WH02",
                    StockTransferLinesBinAllocations: null)
            ]
        );

        _gatewayMock
            .Setup(g => g.CreateStockTransferWithBinsAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SAP unavailable"));

        var act = () => _sut.ExecuteAsync(request);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("SAP unavailable");
    }
}
