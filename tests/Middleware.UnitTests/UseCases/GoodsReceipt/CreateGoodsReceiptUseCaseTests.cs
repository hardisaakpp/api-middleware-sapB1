using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.GoodsReceipt;

namespace Middleware.UnitTests.UseCases.GoodsReceipt;

public sealed class CreateGoodsReceiptUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly CreateGoodsReceiptUseCase _sut;

    public CreateGoodsReceiptUseCaseTests()
    {
        _sut = new CreateGoodsReceiptUseCase(_gatewayMock.Object, NullLogger<CreateGoodsReceiptUseCase>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_ReturnsDocumentNumbers()
    {
        var request = new CreateGoodsReceiptRequest(
            VendorCode: "V001",
            DocDate: new DateOnly(2024, 6, 1),
            Comments: "Test receipt",
            Lines: [new GoodsReceiptLineDto("ITEM001", 10m, 50m, "WH01")]
        );

        _gatewayMock
            .Setup(g => g.CreateGoodsReceiptAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentCreatedResult(55, 3001));

        var result = await _sut.ExecuteAsync(request);

        result.DocEntry.Should().Be(55);
        result.DocNum.Should().Be(3001);
    }
}
