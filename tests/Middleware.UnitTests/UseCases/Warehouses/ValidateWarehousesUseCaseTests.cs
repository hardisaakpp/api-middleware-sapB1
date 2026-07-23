using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Validations;
using Middleware.Application.UseCases.Warehouses;

namespace Middleware.UnitTests.UseCases.Warehouses;

public sealed class ValidateWarehousesUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();

    private ValidateWarehousesUseCase CreateSut(ValidationSettings? settings = null) =>
        new(_gatewayMock.Object,
            Options.Create(settings ?? new ValidationSettings()),
            NullLogger<ValidateWarehousesUseCase>.Instance);

    private static ValidateWarehousesRequest Request(params string[] codes) => new(codes);

    [Fact]
    public async Task ExecuteAsync_ExistingAndMissingCodes_MapsBoth()
    {
        _gatewayMock
            .Setup(g => g.GetWarehousesByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WarehouseResponse("RL-CD", "BODEGA ROLAND CD")]);

        var result = await CreateSut().ExecuteAsync(Request("RL-CD", "NOEXISTE"));

        result.Warehouses.Should().HaveCount(2);
        result.Warehouses[0].Should().BeEquivalentTo(
            new WarehouseValidationResult("RL-CD", Exists: true, "BODEGA ROLAND CD", ErrorType: null));
        result.Warehouses[1].Should().BeEquivalentTo(
            new WarehouseValidationResult("NOEXISTE", Exists: false, WhsName: null, ValidationErrorTypes.NotFound));
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateCodes_AreDeduplicated()
    {
        _gatewayMock
            .Setup(g => g.GetWarehousesByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WarehouseResponse("RL-CD", "BODEGA ROLAND CD")]);

        var result = await CreateSut().ExecuteAsync(Request("RL-CD", "RL-CD", " RL-CD "));

        result.Warehouses.Should().ContainSingle();
        _gatewayMock.Verify(g => g.GetWarehousesByCodesAsync(
            It.Is<IReadOnlyList<string>>(c => c.Count == 1 && c[0] == "RL-CD"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MoreCodesThanChunkSize_SplitsIntoChunks()
    {
        var settings = new ValidationSettings { SapChunkSize = 2, MaxParallelChunks = 1 };
        var codes = new[] { "A", "B", "C", "D", "E" };

        _gatewayMock
            .Setup(g => g.GetWarehousesByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> chunk, CancellationToken _) =>
                chunk.Select(c => new WarehouseResponse(c, $"Bodega {c}")).ToList());

        var result = await CreateSut(settings).ExecuteAsync(Request(codes));

        result.Warehouses.Should().HaveCount(5).And.OnlyContain(w => w.Exists && w.ErrorType == null);
        // 5 codes with chunk size 2 → 3 SL calls
        _gatewayMock.Verify(g => g.GetWarehousesByCodesAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ExecuteAsync_OneChunkFails_MarksOnlyThatChunkAsConnectionError()
    {
        var settings = new ValidationSettings { SapChunkSize = 2, MaxParallelChunks = 1 };

        _gatewayMock
            .Setup(g => g.GetWarehousesByCodesAsync(
                It.Is<IReadOnlyList<string>>(c => c.Contains("C")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SL down"));
        _gatewayMock
            .Setup(g => g.GetWarehousesByCodesAsync(
                It.Is<IReadOnlyList<string>>(c => !c.Contains("C")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> chunk, CancellationToken _) =>
                chunk.Select(c => new WarehouseResponse(c, $"Bodega {c}")).ToList());

        var result = await CreateSut(settings).ExecuteAsync(Request("A", "B", "C", "D"));

        result.Warehouses.Where(w => w.WhsCode is "A" or "B")
            .Should().OnlyContain(w => w.Exists && w.ErrorType == null);
        result.Warehouses.Where(w => w.WhsCode is "C" or "D")
            .Should().OnlyContain(w => !w.Exists && w.ErrorType == ValidationErrorTypes.ConnectionError);
    }

    [Fact]
    public async Task ExecuteAsync_NonTransportSapError_ReturnsErrorWithMessage()
    {
        _gatewayMock
            .Setup(g => g.GetWarehousesByCodesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SL rejected the query"));

        var result = await CreateSut().ExecuteAsync(Request("RL-CD"));

        var warehouse = result.Warehouses.Should().ContainSingle().Subject;
        warehouse.ErrorType.Should().Be(ValidationErrorTypes.Error);
        warehouse.ErrorMessage.Should().Be("SL rejected the query");
    }
}
