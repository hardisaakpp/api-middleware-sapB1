using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Validations;

namespace Middleware.UnitTests.UseCases.Validations;

public sealed class ValidatePurchaseOrderUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();

    private ValidatePurchaseOrderUseCase CreateSut(ValidationSettings? settings = null) =>
        new(_gatewayMock.Object,
            Options.Create(settings ?? new ValidationSettings()),
            NullLogger<ValidatePurchaseOrderUseCase>.Instance);

    private static SapItemValidationData Item(string code, params SapItemWarehouseData[] warehouses) =>
        new(code, $"Nombre {code}", PurchaseItem: true, Valid: true, Frozen: false,
            InventoryUoM: null, Warehouses: warehouses);

    private static ValidatePurchaseOrderRequest Request(string? cardCode, params (string Item, string Wh)[] lines) =>
        new(cardCode, lines.Select(l => new ValidatePurchaseOrderLineDto(l.Item, l.Wh)).ToList());

    [Fact]
    public async Task ExecuteAsync_ItemWithWarehouse_MapsAllFields()
    {
        _gatewayMock
            .Setup(g => g.GetItemsForValidationAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Item("A1", new SapItemWarehouseData("WH01", 120m, 20m, Locked: false))]);

        var result = await CreateSut().ExecuteAsync(Request(null, ("A1", "WH01")));

        result.BusinessPartner.Should().BeNull();
        var item = result.Items.Should().ContainSingle().Subject;
        item.Should().BeEquivalentTo(new ItemValidationResult(
            "A1", "WH01", Exists: true, "Nombre A1",
            PurchaseItem: true, Valid: true, Frozen: false,
            WarehouseConfigured: true, InStock: 120m, IsCommitted: 20m,
            UomCode: null, Locked: false, ErrorType: null));
    }

    [Fact]
    public async Task ExecuteAsync_ItemMissingInSap_ReturnsItemNotFound()
    {
        _gatewayMock
            .Setup(g => g.GetItemsForValidationAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut().ExecuteAsync(Request(null, ("NOPE", "WH01")));

        var item = result.Items.Should().ContainSingle().Subject;
        item.Exists.Should().BeFalse();
        item.ErrorType.Should().Be(ValidationErrorTypes.ItemNotFound);
    }

    [Fact]
    public async Task ExecuteAsync_ItemWithoutThatWarehouse_ReturnsWarehouseNotFound()
    {
        _gatewayMock
            .Setup(g => g.GetItemsForValidationAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Item("A1", new SapItemWarehouseData("OTRA", 5m, 0m, false))]);

        var result = await CreateSut().ExecuteAsync(Request(null, ("A1", "WH01")));

        var item = result.Items.Should().ContainSingle().Subject;
        item.Exists.Should().BeTrue();
        item.WarehouseConfigured.Should().BeFalse();
        item.InStock.Should().Be(0m);
        item.ErrorType.Should().Be(ValidationErrorTypes.WarehouseNotFound);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicatePairs_AreDeduplicated()
    {
        _gatewayMock
            .Setup(g => g.GetItemsForValidationAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Item("A1",
                new SapItemWarehouseData("WH01", 10m, 0m, false),
                new SapItemWarehouseData("WH02", 3m, 0m, false))]);

        var result = await CreateSut().ExecuteAsync(
            Request(null, ("A1", "WH01"), ("A1", "WH01"), ("A1", "WH02")));

        result.Items.Should().HaveCount(2);
        _gatewayMock.Verify(g => g.GetItemsForValidationAsync(
            It.Is<IReadOnlyList<string>>(c => c.Count == 1 && c[0] == "A1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MoreItemsThanChunkSize_SplitsIntoChunks()
    {
        var settings = new ValidationSettings { SapChunkSize = 2, MaxParallelChunks = 1 };
        var codes = new[] { "A", "B", "C", "D", "E" };

        _gatewayMock
            .Setup(g => g.GetItemsForValidationAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> chunk, CancellationToken _) =>
                chunk.Select(c => Item(c, new SapItemWarehouseData("WH01", 1m, 0m, false))).ToList());

        var result = await CreateSut(settings).ExecuteAsync(
            Request(null, codes.Select(c => (c, "WH01")).ToArray()));

        result.Items.Should().HaveCount(5);
        result.Items.Should().OnlyContain(i => i.ErrorType == null && i.WarehouseConfigured);
        // 5 items with chunk size 2 → 3 SL calls
        _gatewayMock.Verify(g => g.GetItemsForValidationAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ExecuteAsync_OneChunkFails_MarksOnlyThatChunkAsConnectionError()
    {
        var settings = new ValidationSettings { SapChunkSize = 2, MaxParallelChunks = 1 };

        _gatewayMock
            .Setup(g => g.GetItemsForValidationAsync(
                It.Is<IReadOnlyList<string>>(c => c.Contains("C")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SL down"));
        _gatewayMock
            .Setup(g => g.GetItemsForValidationAsync(
                It.Is<IReadOnlyList<string>>(c => !c.Contains("C")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> chunk, CancellationToken _) =>
                chunk.Select(c => Item(c, new SapItemWarehouseData("WH01", 1m, 0m, false))).ToList());

        var result = await CreateSut(settings).ExecuteAsync(
            Request(null, ("A", "WH01"), ("B", "WH01"), ("C", "WH01"), ("D", "WH01")));

        result.Items.Where(i => i.ItemCode is "A" or "B")
            .Should().OnlyContain(i => i.ErrorType == null && i.Exists);
        result.Items.Where(i => i.ItemCode is "C" or "D")
            .Should().OnlyContain(i => i.ErrorType == ValidationErrorTypes.ConnectionError && !i.Exists);
    }

    [Fact]
    public async Task ExecuteAsync_NonTransportSapError_ReturnsErrorWithMessage()
    {
        _gatewayMock
            .Setup(g => g.GetItemsForValidationAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SL rejected the query"));

        var result = await CreateSut().ExecuteAsync(Request(null, ("A", "WH01")));

        var item = result.Items.Should().ContainSingle().Subject;
        item.ErrorType.Should().Be(ValidationErrorTypes.Error);
        item.ErrorMessage.Should().Be("SL rejected the query");
    }

    [Fact]
    public async Task ExecuteAsync_WithCardCode_ValidatesBusinessPartner()
    {
        _gatewayMock
            .Setup(g => g.GetItemsForValidationAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _gatewayMock
            .Setup(g => g.GetBusinessPartnerAsync("P001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessPartnerResponse(
                "P001", "PROVEEDOR X", "cSupplier", null, null, null, Valid: true, Frozen: false));

        var result = await CreateSut().ExecuteAsync(Request("P001", ("A", "WH01")));

        result.BusinessPartner.Should().NotBeNull();
        result.BusinessPartner!.Exists.Should().BeTrue();
        result.BusinessPartner.CardName.Should().Be("PROVEEDOR X");
        result.BusinessPartner.Valid.Should().BeTrue();
        result.BusinessPartner.ErrorType.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_BusinessPartnerMissing_ReturnsNotFound()
    {
        _gatewayMock
            .Setup(g => g.GetItemsForValidationAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _gatewayMock
            .Setup(g => g.GetBusinessPartnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BusinessPartnerResponse?)null);

        var result = await CreateSut().ExecuteAsync(Request("NOPE", ("A", "WH01")));

        result.BusinessPartner!.Exists.Should().BeFalse();
        result.BusinessPartner.ErrorType.Should().Be(ValidationErrorTypes.NotFound);
    }

    [Fact]
    public async Task ExecuteAsync_BusinessPartnerConnectionFailure_DoesNotFailBatch()
    {
        _gatewayMock
            .Setup(g => g.GetItemsForValidationAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Item("A", new SapItemWarehouseData("WH01", 1m, 0m, false))]);
        _gatewayMock
            .Setup(g => g.GetBusinessPartnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SL down"));

        var result = await CreateSut().ExecuteAsync(Request("P001", ("A", "WH01")));

        result.BusinessPartner!.ErrorType.Should().Be(ValidationErrorTypes.ConnectionError);
        result.Items.Should().ContainSingle().Which.ErrorType.Should().BeNull();
    }
}
