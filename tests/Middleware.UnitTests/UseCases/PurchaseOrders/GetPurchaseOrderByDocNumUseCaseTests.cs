using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.PurchaseOrders;
using Middleware.Application.UseCases.Validations;

namespace Middleware.UnitTests.UseCases.PurchaseOrders;

public sealed class GetPurchaseOrderByDocNumUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();

    private GetPurchaseOrderByDocNumUseCase CreateSut(ValidationSettings? settings = null) =>
        new(_gatewayMock.Object,
            Options.Create(settings ?? new ValidationSettings()),
            NullLogger<GetPurchaseOrderByDocNumUseCase>.Instance);

    private static ItemResponse Item(string code, string? codeBars) =>
        new(code, $"Nombre {code}", InventoryUoM: null, OnHand: 10m,
            PurchaseItem: true, SalesItem: true, InventoryItem: true, Valid: true, Frozen: false,
            ItemType: "itItems", PackCode: null, GroupCode: null, GroupName: null,
            PreferredVendorCode: null, Pvp: null, CodeBars: codeBars, Marca: null);

    [Fact]
    public async Task ExecuteAsync_DocNumResolves_EnrichesLinesWithCodeBars()
    {
        var order = new PurchaseOrderResponse(
            DocEntry: 99,
            DocNum: 5000,
            VendorCode: "V001",
            DocDate: "2024-06-01",
            DocStatus: "bost_Open",
            Lines: [new PurchaseOrderLineResponse(0, "ITEM001", 10m, 10m, 100m, "WH01")]
        );

        _gatewayMock
            .Setup(g => g.GetPurchaseOrderDocEntryByDocNumAsync(5000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(99);
        _gatewayMock
            .Setup(g => g.GetPurchaseOrderAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _gatewayMock
            .Setup(g => g.GetItemsByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Item("ITEM001", "7501234567890")]);

        var result = await CreateSut().ExecuteAsync(new GetPurchaseOrderByDocNumRequest(5000));

        result.Should().NotBeNull();
        result!.DocEntry.Should().Be(99);
        result.Lines.Should().ContainSingle();
        result.Lines[0].CodeBars.Should().Be("7501234567890");
    }

    [Fact]
    public async Task ExecuteAsync_DocNumNotFound_ReturnsNullWithoutCallingSapForOrderOrItems()
    {
        _gatewayMock
            .Setup(g => g.GetPurchaseOrderDocEntryByDocNumAsync(12345, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);

        var result = await CreateSut().ExecuteAsync(new GetPurchaseOrderByDocNumRequest(12345));

        result.Should().BeNull();
        _gatewayMock.Verify(g => g.GetPurchaseOrderAsync(
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _gatewayMock.Verify(g => g.GetItemsByCodesAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MoreItemCodesThanChunkSize_CallsGetItemsByCodesAsyncMoreThanOnce()
    {
        var settings = new ValidationSettings { SapChunkSize = 2, MaxParallelChunks = 1 };
        var order = new PurchaseOrderResponse(
            DocEntry: 99,
            DocNum: 5000,
            VendorCode: "V001",
            DocDate: "2024-06-01",
            DocStatus: "bost_Open",
            Lines:
            [
                new PurchaseOrderLineResponse(0, "A", 1m, 1m, 10m, "WH01"),
                new PurchaseOrderLineResponse(1, "B", 1m, 1m, 10m, "WH01"),
                new PurchaseOrderLineResponse(2, "C", 1m, 1m, 10m, "WH01"),
                new PurchaseOrderLineResponse(3, "D", 1m, 1m, 10m, "WH01"),
                new PurchaseOrderLineResponse(4, "E", 1m, 1m, 10m, "WH01")
            ]
        );

        _gatewayMock
            .Setup(g => g.GetPurchaseOrderDocEntryByDocNumAsync(5000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(99);
        _gatewayMock
            .Setup(g => g.GetPurchaseOrderAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _gatewayMock
            .Setup(g => g.GetItemsByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> chunk, CancellationToken _) =>
                chunk.Select(c => Item(c, $"BAR-{c}")).ToList());

        var result = await CreateSut(settings).ExecuteAsync(new GetPurchaseOrderByDocNumRequest(5000));

        result.Should().NotBeNull();
        result!.Lines.Should().HaveCount(5);
        result.Lines.Should().OnlyContain(l => l.CodeBars == $"BAR-{l.ItemCode}");
        // 5 distinct item codes with chunk size 2 → 3 SL calls
        _gatewayMock.Verify(g => g.GetItemsByCodesAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
}
