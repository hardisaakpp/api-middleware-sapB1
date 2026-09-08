using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.WebPos;
using Middleware.Domain.Enums;

namespace Middleware.UnitTests.UseCases.WebPos;

public sealed class NotifyWebPosStockTransferUseCaseTests
{
    private readonly Mock<ISapGateway> _sapGatewayMock = new();
    private readonly Mock<IWebPosGateway> _webPosGatewayMock = new();
    private readonly Mock<IPosSyncStatusRepository> _posSyncStatusRepositoryMock = new();
    private readonly NotifyWebPosStockTransferUseCase _sut;

    public NotifyWebPosStockTransferUseCaseTests()
    {
        _sut = new NotifyWebPosStockTransferUseCase(
            _sapGatewayMock.Object,
            _webPosGatewayMock.Object,
            _posSyncStatusRepositoryMock.Object,
            NullLogger<NotifyWebPosStockTransferUseCase>.Instance);
    }

    private static NotifyWebPosStockTransferRequest Request(string fromWarehouse = "WH01", string toWarehouse = "WH02") =>
        new(42, 1001, fromWarehouse, toWarehouse, [new NotifyWebPosStockTransferLine("ITEM001", 5m)]);

    [Fact]
    public async Task ExecuteAsync_NeitherWarehouseIsPosEnabled_SkipsWithoutCallingWebPos()
    {
        _sapGatewayMock
            .Setup(g => g.GetWarehousesPosInfoForTestCompanyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, WarehousePosInfo>
            {
                ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: false, CodBusiness: null),
                ["WH02"] = new WarehousePosInfo("WH02", IsPosEnabled: false, CodBusiness: null)
            });

        var result = await _sut.ExecuteAsync(Request());

        result.Status.Should().Be(WebPosSyncStatus.SkippedNotPos);
        _webPosGatewayMock.Verify(
            g => g.UpdateInventoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _posSyncStatusRepositoryMock.Verify(r => r.UpsertAsync("ST", 42, It.IsAny<int?>(), "SKIPPED_NOT_POS", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BothWarehousesShareCodBusiness_SendsOneCallWithTwoStoreEntries()
    {
        _sapGatewayMock
            .Setup(g => g.GetWarehousesPosInfoForTestCompanyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, WarehousePosInfo>
            {
                ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: true, CodBusiness: "1"),
                ["WH02"] = new WarehousePosInfo("WH02", IsPosEnabled: true, CodBusiness: "1")
            });

        _sapGatewayMock
            .Setup(g => g.GetItemStockForTestCompanyAsync("ITEM001", "WH01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemStockResponse("ITEM001", "WH01", 3m, 0m, "UNIDAD", false));
        _sapGatewayMock
            .Setup(g => g.GetItemStockForTestCompanyAsync("ITEM001", "WH02", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemStockResponse("ITEM001", "WH02", 7m, 0m, "UNIDAD", false));

        _webPosGatewayMock
            .Setup(g => g.UpdateInventoryAsync("1", "ITEM001", It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosUpdateOutcome(true));

        var result = await _sut.ExecuteAsync(Request());

        result.Status.Should().Be(WebPosSyncStatus.Synced);
        _webPosGatewayMock.Verify(
            g => g.UpdateInventoryAsync(
                "1", "ITEM001",
                It.Is<IReadOnlyList<WebPosStoreStock>>(s =>
                    s.Count == 2 &&
                    s.Any(x => x.SapStoreCode == "WH01" && x.Stock == 3m) &&
                    s.Any(x => x.SapStoreCode == "WH02" && x.Stock == 7m)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _posSyncStatusRepositoryMock.Verify(r => r.UpsertAsync("ST", 42, It.IsAny<int?>(), "SYNCED", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WarehousesBelongToDifferentBusinesses_SendsTwoSeparateCalls()
    {
        _sapGatewayMock
            .Setup(g => g.GetWarehousesPosInfoForTestCompanyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, WarehousePosInfo>
            {
                ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: true, CodBusiness: "1"),
                ["WH02"] = new WarehousePosInfo("WH02", IsPosEnabled: true, CodBusiness: "2")
            });

        _sapGatewayMock
            .Setup(g => g.GetItemStockForTestCompanyAsync("ITEM001", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string itemCode, string wh, CancellationToken _) => new ItemStockResponse(itemCode, wh, 1m, 0m, "UNIDAD", false));

        _webPosGatewayMock
            .Setup(g => g.UpdateInventoryAsync(It.IsAny<string>(), "ITEM001", It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosUpdateOutcome(true));

        var result = await _sut.ExecuteAsync(Request());

        result.Status.Should().Be(WebPosSyncStatus.Synced);
        _webPosGatewayMock.Verify(g => g.UpdateInventoryAsync("1", "ITEM001", It.Is<IReadOnlyList<WebPosStoreStock>>(s => s.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
        _webPosGatewayMock.Verify(g => g.UpdateInventoryAsync("2", "ITEM001", It.Is<IReadOnlyList<WebPosStoreStock>>(s => s.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WebPosRejectsBusinessRule_ReturnsRejectedWithoutThrowing()
    {
        _sapGatewayMock
            .Setup(g => g.GetWarehousesPosInfoForTestCompanyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, WarehousePosInfo>
            {
                ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: true, CodBusiness: "1"),
                ["WH02"] = new WarehousePosInfo("WH02", IsPosEnabled: true, CodBusiness: "1")
            });

        _sapGatewayMock
            .Setup(g => g.GetItemStockForTestCompanyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemStockResponse("ITEM001", "WH01", 0m, 0m, "UNIDAD", false));

        _webPosGatewayMock
            .Setup(g => g.UpdateInventoryAsync("1", "ITEM001", It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosUpdateOutcome(false, "El producto no existe."));

        var result = await _sut.ExecuteAsync(Request());

        result.Status.Should().Be(WebPosSyncStatus.Rejected);
        result.Reason.Should().Contain("El producto no existe.");
        _posSyncStatusRepositoryMock.Verify(
            r => r.UpsertAsync("ST", 42, It.IsAny<int?>(), "REJECTED", It.Is<string>(reason => reason!.Contains("El producto no existe.")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WebPosRejectsProductNotFound_RetriesOnceWithEnrichmentAndSucceeds()
    {
        _sapGatewayMock
            .Setup(g => g.GetWarehousesPosInfoForTestCompanyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, WarehousePosInfo>
            {
                ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: true, CodBusiness: "1"),
                ["WH02"] = new WarehousePosInfo("WH02", IsPosEnabled: true, CodBusiness: "1")
            });

        _sapGatewayMock
            .Setup(g => g.GetItemStockForTestCompanyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemStockResponse("ITEM001", "WH01", 5m, 0m, "UNIDAD", false));

        _sapGatewayMock
            .Setup(g => g.GetItemPosEnrichmentForTestCompanyAsync("ITEM001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemPosEnrichment("10.270", "rolando", "1", ["786137983", "786137984"]));

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
                stock.Should().OnlyContain(s => s.Barcodes!.SequenceEqual(new[] { "786137983", "786137984" }));
                return new WebPosUpdateOutcome(true);
            });

        var result = await _sut.ExecuteAsync(Request());

        result.Status.Should().Be(WebPosSyncStatus.Synced);
        calls.Should().Be(2);
        _sapGatewayMock.Verify(g => g.GetItemPosEnrichmentForTestCompanyAsync("ITEM001", It.IsAny<CancellationToken>()), Times.Once);
        _posSyncStatusRepositoryMock.Verify(r => r.UpsertAsync("ST", 42, It.IsAny<int?>(), "SYNCED", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WebPosRejectsProductNotFound_EnrichmentRetryAlsoFails_ReturnsRejected()
    {
        _sapGatewayMock
            .Setup(g => g.GetWarehousesPosInfoForTestCompanyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, WarehousePosInfo>
            {
                ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: true, CodBusiness: "1"),
                ["WH02"] = new WarehousePosInfo("WH02", IsPosEnabled: true, CodBusiness: "1")
            });

        _sapGatewayMock
            .Setup(g => g.GetItemStockForTestCompanyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemStockResponse("ITEM001", "WH01", 5m, 0m, "UNIDAD", false));

        _sapGatewayMock
            .Setup(g => g.GetItemPosEnrichmentForTestCompanyAsync("ITEM001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemPosEnrichment(null, null, null, []));

        _webPosGatewayMock
            .Setup(g => g.UpdateInventoryAsync("1", "ITEM001", It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosUpdateOutcome(false, "El producto no existe."));

        var result = await _sut.ExecuteAsync(Request());

        result.Status.Should().Be(WebPosSyncStatus.Rejected);
        _webPosGatewayMock.Verify(
            g => g.UpdateInventoryAsync("1", "ITEM001", It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_WebPosRejectsForAnyOtherReason_NeverCallsEnrichment()
    {
        _sapGatewayMock
            .Setup(g => g.GetWarehousesPosInfoForTestCompanyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, WarehousePosInfo>
            {
                ["WH01"] = new WarehousePosInfo("WH01", IsPosEnabled: true, CodBusiness: "1"),
                ["WH02"] = new WarehousePosInfo("WH02", IsPosEnabled: true, CodBusiness: "1")
            });

        _sapGatewayMock
            .Setup(g => g.GetItemStockForTestCompanyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemStockResponse("ITEM001", "WH01", 5m, 0m, "UNIDAD", false));

        _webPosGatewayMock
            .Setup(g => g.UpdateInventoryAsync("1", "ITEM001", It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebPosUpdateOutcome(false, "La tienda no pertenece al negocio."));

        var result = await _sut.ExecuteAsync(Request());

        result.Status.Should().Be(WebPosSyncStatus.Rejected);
        _sapGatewayMock.Verify(g => g.GetItemPosEnrichmentForTestCompanyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _webPosGatewayMock.Verify(
            g => g.UpdateInventoryAsync("1", "ITEM001", It.IsAny<IReadOnlyList<WebPosStoreStock>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
