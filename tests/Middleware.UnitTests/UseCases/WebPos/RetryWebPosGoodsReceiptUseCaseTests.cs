using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.WebPos;
using Middleware.Domain.Enums;

namespace Middleware.UnitTests.UseCases.WebPos;

public sealed class RetryWebPosGoodsReceiptUseCaseTests
{
    private readonly Mock<ISapGateway> _sapGatewayMock = new();
    private readonly Mock<IWebPosGoodsReceiptNotifier> _notifierMock = new();
    private readonly RetryWebPosGoodsReceiptUseCase _sut;

    public RetryWebPosGoodsReceiptUseCaseTests()
    {
        _sut = new RetryWebPosGoodsReceiptUseCase(
            _sapGatewayMock.Object, _notifierMock.Object, NullLogger<RetryWebPosGoodsReceiptUseCase>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_ReceiptNotFoundInSap_ThrowsKeyNotFound()
    {
        _sapGatewayMock
            .Setup(g => g.GetGoodsReceiptAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoodsReceiptDocumentResponse?)null);

        var act = () => _sut.ExecuteAsync(new RetryWebPosGoodsReceiptRequest(999));

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _notifierMock.Verify(
            n => n.NotifyAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<NotifyWebPosGoodsReceiptLine>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ReceiptExists_RebuildsPayloadFromSapAndNotifies()
    {
        _sapGatewayMock
            .Setup(g => g.GetGoodsReceiptAsync(55, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoodsReceiptDocumentResponse(55, 3001,
            [
                new GoodsReceiptDocumentLine("ITEM001", 10m, "WH01"),
                new GoodsReceiptDocumentLine("ITEM002", 4m, "WH02")
            ]));

        _notifierMock
            .Setup(n => n.NotifyAsync(55, 3001, It.IsAny<IReadOnlyList<NotifyWebPosGoodsReceiptLine>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosSyncInfo(WebPosSyncStatus.Synced));

        var result = await _sut.ExecuteAsync(new RetryWebPosGoodsReceiptRequest(55));

        result.Status.Should().Be(WebPosSyncStatus.Synced);
        _notifierMock.Verify(n => n.NotifyAsync(
            55, 3001,
            It.Is<IReadOnlyList<NotifyWebPosGoodsReceiptLine>>(lines =>
                lines.Count == 2 &&
                lines.Any(l => l.ItemCode == "ITEM001" && l.Quantity == 10m && l.WarehouseCode == "WH01") &&
                lines.Any(l => l.ItemCode == "ITEM002" && l.Quantity == 4m && l.WarehouseCode == "WH02")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
