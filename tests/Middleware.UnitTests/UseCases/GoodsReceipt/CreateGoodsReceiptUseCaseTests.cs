using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.GoodsReceipt;
using Middleware.Domain.Enums;

namespace Middleware.UnitTests.UseCases.GoodsReceipt;

public sealed class CreateGoodsReceiptUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly Mock<IWebPosGoodsReceiptNotifier> _webPosNotifierMock = new();
    private readonly CreateGoodsReceiptUseCase _sut;

    public CreateGoodsReceiptUseCaseTests()
    {
        _sut = new CreateGoodsReceiptUseCase(
            _gatewayMock.Object, _webPosNotifierMock.Object, NullLogger<CreateGoodsReceiptUseCase>.Instance);
    }

    private static CreateGoodsReceiptRequest Request() => new(
        VendorCode: "V001",
        DocDate: new DateOnly(2024, 6, 1),
        Comments: "Test receipt",
        Lines: [new GoodsReceiptLineDto("ITEM001", 10m, 50m, "WH01")]
    );

    [Fact]
    public async Task ExecuteAsync_ValidRequest_ReturnsDocumentNumbersAndWebPosSync()
    {
        var request = Request();

        _gatewayMock
            .Setup(g => g.CreateGoodsReceiptAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentCreatedResult(55, 3001));
        _webPosNotifierMock
            .Setup(n => n.NotifyAsync(55, 3001, It.IsAny<IReadOnlyList<NotifyWebPosGoodsReceiptLine>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosSyncInfo(WebPosSyncStatus.Synced));

        var result = await _sut.ExecuteAsync(request);

        result.DocEntry.Should().Be(55);
        result.DocNum.Should().Be(3001);
        result.WebPosSync.Should().NotBeNull();
        result.WebPosSync!.Status.Should().Be(WebPosSyncStatus.Synced);
    }

    [Fact]
    public async Task ExecuteAsync_NotifiesWebPosWithLineWarehouses()
    {
        var request = Request();

        _gatewayMock
            .Setup(g => g.CreateGoodsReceiptAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentCreatedResult(55, 3001));
        _webPosNotifierMock
            .Setup(n => n.NotifyAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<NotifyWebPosGoodsReceiptLine>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosSyncInfo(WebPosSyncStatus.SkippedNotPos));

        await _sut.ExecuteAsync(request);

        _webPosNotifierMock.Verify(n => n.NotifyAsync(
            55, 3001,
            It.Is<IReadOnlyList<NotifyWebPosGoodsReceiptLine>>(lines =>
                lines.Count == 1 && lines[0].ItemCode == "ITEM001" && lines[0].WarehouseCode == "WH01" && lines[0].Quantity == 10m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WebPosRejected_StillReturnsCreatedDocument()
    {
        var request = Request();

        _gatewayMock
            .Setup(g => g.CreateGoodsReceiptAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentCreatedResult(55, 3001));
        _webPosNotifierMock
            .Setup(n => n.NotifyAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<NotifyWebPosGoodsReceiptLine>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosSyncInfo(WebPosSyncStatus.Rejected, "El producto no existe."));

        var result = await _sut.ExecuteAsync(request);

        result.DocEntry.Should().Be(55);
        result.WebPosSync!.Status.Should().Be(WebPosSyncStatus.Rejected);
        result.WebPosSync.Reason.Should().Be("El producto no existe.");
    }
}
