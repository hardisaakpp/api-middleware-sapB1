using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.PurchaseOrders;

namespace Middleware.UnitTests.UseCases.PurchaseOrders;

public sealed class GetPurchaseOrdersPendientesUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly GetPurchaseOrdersPendientesUseCase _sut;

    public GetPurchaseOrdersPendientesUseCaseTests()
    {
        _sut = new GetPurchaseOrdersPendientesUseCase(_gatewayMock.Object, NullLogger<GetPurchaseOrdersPendientesUseCase>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_PasaElRequestSinModificarAlGateway()
    {
        var desde = new DateTime(2026, 9, 1);
        var hasta = new DateTime(2026, 9, 17);
        var almacenes = new List<string> { "OUT-CD", "RL-SHO" };
        var request = new GetPurchaseOrdersPendientesRequest(desde, hasta, almacenes);

        var expected = new List<PurchaseOrderResponse>
        {
            new(
                DocEntry: 46049,
                DocNum: 24747,
                VendorCode: "P1790389219001",
                DocDate: "2026-09-13",
                DocStatus: "bost_Open",
                Lines: [new PurchaseOrderLineResponse(0, "R124.1012.BD", 40m, 40m, 2.59m, "OUT-CD")]
            )
        };

        _gatewayMock
            .Setup(g => g.GetPurchaseOrdersPendientesAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _sut.ExecuteAsync(request);

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task ExecuteAsync_GatewayThrows_PropagatesException()
    {
        var request = new GetPurchaseOrdersPendientesRequest(DateTime.Today, DateTime.Today, ["OUT-CD"]);

        _gatewayMock
            .Setup(g => g.GetPurchaseOrdersPendientesAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SAP no disponible"));

        var act = () => _sut.ExecuteAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
