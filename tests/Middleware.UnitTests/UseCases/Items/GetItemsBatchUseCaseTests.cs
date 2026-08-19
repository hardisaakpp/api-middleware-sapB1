using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Items;
using Middleware.Application.UseCases.Validations;

namespace Middleware.UnitTests.UseCases.Items;

public sealed class GetItemsBatchUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();

    private GetItemsBatchUseCase CreateSut(ValidationSettings? settings = null) =>
        new(_gatewayMock.Object,
            Options.Create(settings ?? new ValidationSettings()),
            NullLogger<GetItemsBatchUseCase>.Instance);

    private static ItemResponse Item(string code, string? groupName = "R-MEDIAS") =>
        new(code, $"Nombre {code}", InventoryUoM: null, OnHand: 10m,
            PurchaseItem: true, SalesItem: true, InventoryItem: true, Valid: true, Frozen: false,
            ItemType: "itItems", PackCode: "X3", GroupCode: 103, GroupName: groupName,
            PreferredVendorCode: null, Pvp: null, CodeBars: null, Marca: null);

    [Fact]
    public async Task ExecuteAsync_FoundAndMissingCodes_SplitsIntoItemsAndNotFound()
    {
        _gatewayMock
            .Setup(g => g.GetItemsByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Item("A1")]);

        var result = await CreateSut().ExecuteAsync(new GetItemsBatchRequest(["A1", "NO-EXISTE"]));

        result.Items.Should().ContainSingle().Which.ItemCode.Should().Be("A1");
        result.Items[0].GroupName.Should().Be("R-MEDIAS");
        result.NotFound.Should().ContainSingle().Which.Should().Be("NO-EXISTE");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyList_ReturnsEmptyResponseWithoutCallingSap()
    {
        var result = await CreateSut().ExecuteAsync(new GetItemsBatchRequest([]));

        result.Items.Should().BeEmpty();
        result.NotFound.Should().BeEmpty();
        _gatewayMock.Verify(g => g.GetItemsByCodesAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateCodes_AreDeduplicated()
    {
        _gatewayMock
            .Setup(g => g.GetItemsByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Item("A1")]);

        var result = await CreateSut().ExecuteAsync(new GetItemsBatchRequest(["A1", "a1", " A1 "]));

        result.Items.Should().ContainSingle();
        _gatewayMock.Verify(g => g.GetItemsByCodesAsync(
            It.Is<IReadOnlyList<string>>(c => c.Count == 1 && c[0] == "A1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MoreCodesThanChunkSize_SplitsIntoChunks()
    {
        var settings = new ValidationSettings { SapChunkSize = 2, MaxParallelChunks = 1 };
        var codes = new[] { "A", "B", "C", "D", "E" };

        _gatewayMock
            .Setup(g => g.GetItemsByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> chunk, CancellationToken _) =>
                chunk.Select(c => Item(c)).ToList());

        var result = await CreateSut(settings).ExecuteAsync(new GetItemsBatchRequest(codes));

        result.Items.Should().HaveCount(5);
        result.NotFound.Should().BeEmpty();
        // 5 codes with chunk size 2 → 3 SL calls
        _gatewayMock.Verify(g => g.GetItemsByCodesAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ExecuteAsync_ChunkFails_FailsTheWholeBatch()
    {
        var settings = new ValidationSettings { SapChunkSize = 2, MaxParallelChunks = 1 };

        _gatewayMock
            .Setup(g => g.GetItemsByCodesAsync(
                It.Is<IReadOnlyList<string>>(c => c.Contains("C")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SL down"));
        _gatewayMock
            .Setup(g => g.GetItemsByCodesAsync(
                It.Is<IReadOnlyList<string>>(c => !c.Contains("C")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> chunk, CancellationToken _) =>
                chunk.Select(c => Item(c)).ToList());

        var act = () => CreateSut(settings).ExecuteAsync(new GetItemsBatchRequest(["A", "B", "C", "D"]));

        // No silent partial results: a connectivity failure mid-batch fails the request
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
