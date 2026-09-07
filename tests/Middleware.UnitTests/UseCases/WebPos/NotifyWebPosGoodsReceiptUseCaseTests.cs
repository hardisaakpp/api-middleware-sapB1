using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.WebPos;
using Middleware.Domain.Enums;

namespace Middleware.UnitTests.UseCases.WebPos;

public sealed class NotifyWebPosGoodsReceiptUseCaseTests
{
    private readonly Mock<ISapGateway> _sapGatewayMock = new();
    private readonly Mock<IWebPosGateway> _webPosGatewayMock = new();
    private readonly Mock<IPosSyncStatusRepository> _posSyncStatusRepositoryMock = new();
    private readonly NotifyWebPosGoodsReceiptUseCase _sut;

    public NotifyWebPosGoodsReceiptUseCaseTests()
    {
        _sut = new NotifyWebPosGoodsReceiptUseCase(
            _sapGatewayMock.Object,
            _webPosGatewayMock.Object,
            _posSyncStatusRepositoryMock.Object,
            NullLogger<NotifyWebPosGoodsReceiptUseCase>.Instance);
    }

    private static NotifyWebPosGoodsReceiptRequest Request(params NotifyWebPosGoodsReceiptLine[] lines) =>
        new(55, 3001, lines.Length > 0 ? lines : [new NotifyWebPosGoodsReceiptLine("ITEM001", 10m, "WH01")]);

    private void SetupPosInfo(Dictionary<string, WarehousePosInfo> map) =>
        _sapGatewayMock
            .Setup(g => g.GetWarehousesPosInfoAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(map);

    [Fact]
    public async Task ExecuteAsync_NoReceivingWarehouseIsPosEnabled_SkipsWithoutCallingWebPos()
    {
        SetupPosInfo(new Dictionary<string, WarehousePosInfo>
        {
            ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: false, CodBusiness: null)
        });

        var result = await _sut.ExecuteAsync(Request());

        result.Status.Should().Be(WebPosSyncStatus.SkippedNotPos);
        _webPosGatewayMock.Verify(
            g => g.UpdateInventoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _posSyncStatusRepositoryMock.Verify(r => r.UpsertAsync("GR", 55, "SKIPPED_NOT_POS", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PosWarehouse_ReadsStockFromDefaultCompanyAndSyncs()
    {
        SetupPosInfo(new Dictionary<string, WarehousePosInfo>
        {
            ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: true, CodBusiness: "1")
        });

        _sapGatewayMock
            .Setup(g => g.GetItemStockAsync("ITEM001", "WH01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemStockResponse("ITEM001", "WH01", 12m, 0m, "UNIDAD", false));

        _webPosGatewayMock
            .Setup(g => g.UpdateInventoryAsync("1", "ITEM001", It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosUpdateOutcome(true));

        var result = await _sut.ExecuteAsync(Request());

        result.Status.Should().Be(WebPosSyncStatus.Synced);
        _webPosGatewayMock.Verify(
            g => g.UpdateInventoryAsync(
                "1", "ITEM001",
                It.Is<IReadOnlyList<WebPosStoreStock>>(s => s.Count == 1 && s[0].SapStoreCode == "WH01" && s[0].Stock == 12m),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _posSyncStatusRepositoryMock.Verify(r => r.UpsertAsync("GR", 55, "SYNCED", null, It.IsAny<CancellationToken>()), Times.Once);

        // Todas las lecturas SAP del flujo GR van contra la compañía por defecto, nunca la de test.
        _sapGatewayMock.Verify(g => g.GetItemStockForTestCompanyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _sapGatewayMock.Verify(g => g.GetWarehousesPosInfoForTestCompanyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MixedLines_OnlyPosWarehousesAreNotified()
    {
        SetupPosInfo(new Dictionary<string, WarehousePosInfo>
        {
            ["TIENDA"] = new WarehousePosInfo("TIENDA", IsPosEnabled: true, CodBusiness: "2"),
            ["BODEGA"] = new WarehousePosInfo("BODEGA", IsPosEnabled: false, CodBusiness: null)
        });

        _sapGatewayMock
            .Setup(g => g.GetItemStockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string itemCode, string wh, CancellationToken _) => new ItemStockResponse(itemCode, wh, 4m, 0m, "UNIDAD", false));

        _webPosGatewayMock
            .Setup(g => g.UpdateInventoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosUpdateOutcome(true));

        var result = await _sut.ExecuteAsync(Request(
            new NotifyWebPosGoodsReceiptLine("ITEM001", 5m, "TIENDA"),
            new NotifyWebPosGoodsReceiptLine("ITEM002", 3m, "BODEGA")));

        result.Status.Should().Be(WebPosSyncStatus.Synced);
        _webPosGatewayMock.Verify(
            g => g.UpdateInventoryAsync("2", "ITEM001", It.Is<IReadOnlyList<WebPosStoreStock>>(s => s.Count == 1 && s[0].SapStoreCode == "TIENDA"), It.IsAny<CancellationToken>()),
            Times.Once);
        _webPosGatewayMock.Verify(
            g => g.UpdateInventoryAsync(It.IsAny<string>(), "ITEM002", It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WebPosRejects_ReturnsRejectedAndRecordsReason()
    {
        SetupPosInfo(new Dictionary<string, WarehousePosInfo>
        {
            ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: true, CodBusiness: "1")
        });

        _sapGatewayMock
            .Setup(g => g.GetItemStockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemStockResponse("ITEM001", "WH01", 0m, 0m, "UNIDAD", false));

        _webPosGatewayMock
            .Setup(g => g.UpdateInventoryAsync("1", "ITEM001", It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosUpdateOutcome(false, "Tienda fuera del negocio."));

        var result = await _sut.ExecuteAsync(Request());

        result.Status.Should().Be(WebPosSyncStatus.Rejected);
        result.Reason.Should().Contain("Tienda fuera del negocio.");
        _posSyncStatusRepositoryMock.Verify(
            r => r.UpsertAsync("GR", 55, "REJECTED", It.Is<string>(reason => reason!.Contains("Tienda fuera del negocio.")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ProductNotFound_RetriesOnceWithDefaultCompanyEnrichment()
    {
        SetupPosInfo(new Dictionary<string, WarehousePosInfo>
        {
            ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: true, CodBusiness: "1")
        });

        _sapGatewayMock
            .Setup(g => g.GetItemStockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemStockResponse("ITEM001", "WH01", 5m, 0m, "UNIDAD", false));

        _sapGatewayMock
            .Setup(g => g.GetItemPosEnrichmentAsync("ITEM001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemPosEnrichment("10.270", "rolando", "1", ["786137983"]));

        var calls = 0;
        _webPosGatewayMock
            .Setup(g => g.UpdateInventoryAsync("1", "ITEM001", It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, IReadOnlyList<WebPosStoreStock> stock, CancellationToken _) =>
            {
                calls++;
                if (calls == 1)
                {
                    stock.Should().OnlyContain(s => s.Price == null && s.Brand == null && s.CodeTax == null && s.Barcodes == null);
                    return new WebPosUpdateOutcome(false, "El producto no existe.");
                }

                stock.Should().OnlyContain(s => s.Price == "10.270" && s.Brand == "rolando" && s.CodeTax == "1");
                return new WebPosUpdateOutcome(true);
            });

        var result = await _sut.ExecuteAsync(Request());

        result.Status.Should().Be(WebPosSyncStatus.Synced);
        calls.Should().Be(2);
        _sapGatewayMock.Verify(g => g.GetItemPosEnrichmentAsync("ITEM001", It.IsAny<CancellationToken>()), Times.Once);
        _sapGatewayMock.Verify(g => g.GetItemPosEnrichmentForTestCompanyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _posSyncStatusRepositoryMock.Verify(r => r.UpsertAsync("GR", 55, "SYNCED", null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
