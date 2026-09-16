using FluentAssertions;
using Middleware.Infrastructure.Sap;

namespace Middleware.UnitTests.Sap;

public sealed class SapGatewayParseBankDepositResultTests
{
    [Fact]
    public void ParseBankDepositResult_ConDepositNumber_ExtraeIdCorrectamente()
    {
        var json = """{"DepositNumber": 12345, "AbsEntry": 999}""";

        var resultado = SapGateway.ParseBankDepositResult(json);

        resultado.DocEntry.Should().Be(12345);
        resultado.Mensaje.Should().Be("Depósito registrado exitosamente en SAP.");
    }

    [Fact]
    public void ParseBankDepositResult_SinDepositNumberConAbsEntry_ExtraeIdCorrectamente()
    {
        var json = """{"AbsEntry": 777}""";

        var resultado = SapGateway.ParseBankDepositResult(json);

        resultado.DocEntry.Should().Be(777);
        resultado.Mensaje.Should().Be("Depósito registrado exitosamente en SAP.");
    }

    [Fact]
    public void ParseBankDepositResult_SoloDocEntryLegacy_ExtraeIdCorrectamente()
    {
        var json = """{"DocEntry": 42}""";

        var resultado = SapGateway.ParseBankDepositResult(json);

        resultado.DocEntry.Should().Be(42);
    }

    [Fact]
    public void ParseBankDepositResult_SinIdentificadorValido_LanzaSapResponseException()
    {
        var json = """{"OtherField": "sin identificador"}""";

        var act = () => SapGateway.ParseBankDepositResult(json);

        act.Should().Throw<SapResponseException>()
            .WithMessage("*DepositNumber/AbsEntry/DocEntry*");
    }
}
