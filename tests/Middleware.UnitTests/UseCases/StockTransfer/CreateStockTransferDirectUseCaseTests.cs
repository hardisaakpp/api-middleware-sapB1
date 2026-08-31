using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.StockTransfer;

namespace Middleware.UnitTests.UseCases.StockTransfer;

public sealed class CreateStockTransferDirectUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly CreateStockTransferDirectUseCase _sut;

    public CreateStockTransferDirectUseCaseTests()
    {
        _sut = new CreateStockTransferDirectUseCase(_gatewayMock.Object, NullLogger<CreateStockTransferDirectUseCase>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_ReturnsDocumentNumbers()
    {
        var request = new CreateStockTransferDirectRequest(
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
            StockTransferLines: [new StockTransferDirectLineDto("ITEM001", 10m, "WH02")],
            U_sap_id: null,
            U_GUIA_LAAR: null
        );

        _gatewayMock
            .Setup(g => g.CreateStockTransferDirectAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentCreatedResult(42, 1001));

        var result = await _sut.ExecuteAsync(request);

        result.DocEntry.Should().Be(42);
        result.DocNum.Should().Be(1001);
        _gatewayMock.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_GatewayThrows_PropagatesException()
    {
        var request = new CreateStockTransferDirectRequest(
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
            StockTransferLines: [new StockTransferDirectLineDto("ITEM001", 5m, "WH02")],
            U_sap_id: null,
            U_GUIA_LAAR: null
        );

        _gatewayMock
            .Setup(g => g.CreateStockTransferDirectAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SAP unavailable"));

        var act = () => _sut.ExecuteAsync(request);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("SAP unavailable");
    }
}
