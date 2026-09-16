using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Middleware.Application.Dtos.Requests;
using Middleware.Infrastructure.Configuration;
using Middleware.Infrastructure.Sap;
using Middleware.Infrastructure.WebPos;
using Moq;

namespace Middleware.UnitTests.Sap;

public sealed class SapGatewayCreateBankDepositIdempotencyTests
{
    private readonly Mock<ISapSessionManager> _sessionManager = new();
    private readonly Mock<ISapServiceLayerClient> _client = new();

    public SapGatewayCreateBankDepositIdempotencyTests()
    {
        _sessionManager
            .Setup(s => s.GetSessionTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("token-123");
    }

    private SapGateway CreateGateway()
    {
        var sapSettings = Options.Create(new SapSettings());
        var webPosSettings = Options.Create(new WebPosSettings());

        return new SapGateway(
            _sessionManager.Object,
            _client.Object,
            new SapThrottle(sapSettings),
            new ItemGroupsCache(sapSettings),
            new WarehousePosInfoCache(webPosSettings),
            sapSettings,
            NullLogger<SapGateway>.Instance);
    }

    private static CreateBankDepositRequest Request() => new(
        CodigoAlmacen: "T001",
        FechaCierreCaja: new DateOnly(2026, 9, 1),
        FechaDepositoBanco: new DateOnly(2026, 9, 2),
        CodigoCuentaSAP: "1101010001",
        NumeroComprobante: "DEP00001",
        MontoDepositado: 250.75m,
        Moneda: "USD",
        TipoDeposito: "dtCash",
        CuentaPuente: "_SYS00000001151",
        Responsable: "Juan Perez",
        EsSobrante: false);

    [Fact]
    public async Task CreateBankDepositAsync_DepositoYaExisteEnSap_NoEjecutaPostYDevuelveElExistente()
    {
        _client
            .Setup(c => c.SendAsync(
                HttpMethod.Get, It.Is<string>(r => r.StartsWith("Deposits?$filter")),
                null, "token-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"value":[{"DepositNumber":555,"AbsEntry":9999}]}""");

        var resultado = await CreateGateway().CreateBankDepositAsync(Request(), CancellationToken.None);

        resultado.DocEntry.Should().Be(555);
        resultado.Mensaje.Should().Be("Depósito recuperado previamente de SAP.");

        _client.Verify(c => c.SendAsync(
            HttpMethod.Post, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateBankDepositAsync_DepositoNuevo_ConsultaSapYLuegoEjecutaElPostRegular()
    {
        _client
            .Setup(c => c.SendAsync(
                HttpMethod.Get, It.Is<string>(r => r.StartsWith("Deposits?$filter")),
                null, "token-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"value":[]}""");

        _client
            .Setup(c => c.SendAsync(HttpMethod.Post, "Deposits", It.IsAny<string>(), "token-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"DepositNumber":12345}""");

        var resultado = await CreateGateway().CreateBankDepositAsync(Request(), CancellationToken.None);

        resultado.DocEntry.Should().Be(12345);
        resultado.Mensaje.Should().Be("Depósito registrado exitosamente en SAP.");

        _client.Verify(c => c.SendAsync(
            HttpMethod.Get, It.Is<string>(r => r.StartsWith("Deposits?$filter")), null, "token-123", It.IsAny<CancellationToken>()),
            Times.Once);
        _client.Verify(c => c.SendAsync(
            HttpMethod.Post, "Deposits", It.IsAny<string>(), "token-123", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
