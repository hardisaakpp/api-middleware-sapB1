using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.StockTransfer;
using Middleware.Domain.Enums;

namespace Middleware.UnitTests.UseCases.StockTransfer;

public sealed class CreateStockTransferDirectUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly Mock<IWebPosStockTransferNotifier> _webPosNotifierMock = new();
    private readonly CreateStockTransferDirectUseCase _sut;

    public CreateStockTransferDirectUseCaseTests()
    {
        _webPosNotifierMock
            .Setup(n => n.NotifyAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<NotifyWebPosStockTransferLine>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosSyncInfo(WebPosSyncStatus.Synced));

        _sut = new CreateStockTransferDirectUseCase(
            _gatewayMock.Object, _webPosNotifierMock.Object, NullLogger<CreateStockTransferDirectUseCase>.Instance);
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
        _webPosNotifierMock.Verify(
            n => n.NotifyAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<NotifyWebPosStockTransferLine>>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "WEBPOS must never be notified for a transfer that failed to create in SAP");
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_EmbedsWebPosSyncResultAndNeverRecreatesSapTransferOnWebPosOutcome()
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

        // Even a "bad" outcome from WEBPOS (Error) must never cause the SAP transfer to be
        // re-created — the notifier is expected to be called exactly once, and its result is
        // simply passed through, never triggers a second call to the SAP gateway.
        _webPosNotifierMock
            .Setup(n => n.NotifyAsync(42, 1001, "WH01", "WH02", It.IsAny<IReadOnlyList<NotifyWebPosStockTransferLine>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosSyncInfo(WebPosSyncStatus.Error, "WEBPOS unavailable"));

        var result = await _sut.ExecuteAsync(request);

        result.WebPosSync.Should().BeEquivalentTo(new WebPosSyncInfo(WebPosSyncStatus.Error, "WEBPOS unavailable"));
        _gatewayMock.Verify(g => g.CreateStockTransferDirectAsync(request, It.IsAny<CancellationToken>()), Times.Once);
        _webPosNotifierMock.Verify(
            n => n.NotifyAsync(42, 1001, "WH01", "WH02", It.IsAny<IReadOnlyList<NotifyWebPosStockTransferLine>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
