using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.BankDeposits;

/// <summary>Creates a Bank Deposit (ODPS) for Mabel Trading (Sap:CompanyDb / Sap:Password).</summary>
public sealed class CreateBankDepositUseCase : IUseCase<CreateBankDepositRequest, BankDepositResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreateBankDepositUseCase> _logger;

    public CreateBankDepositUseCase(ISapGateway gateway, ILogger<CreateBankDepositUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<BankDepositResponse> ExecuteAsync(CreateBankDepositRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating Bank Deposit (Mabel Trading) — CodigoAlmacen={CodigoAlmacen}, NumeroComprobante={NumeroComprobante}, Monto={Monto}",
            request.CodigoAlmacen, request.NumeroComprobante, request.MontoDepositado);

        var result = await _gateway.CreateBankDepositAsync(request, cancellationToken);

        _logger.LogInformation("Bank Deposit created (Mabel Trading): DocEntry={DocEntry}", result.DocEntry);

        return new BankDepositResponse(result.DocEntry, result.Mensaje);
    }
}

/// <summary>Creates a Bank Deposit (ODPS) for Comercial Ecuador (Sap:ComercialEcuadorCompanyDb / Sap:ComercialEcuadorPassword).</summary>
public sealed class CreateBankDepositComercialEcuadorUseCase : IUseCase<CreateBankDepositRequest, BankDepositResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreateBankDepositComercialEcuadorUseCase> _logger;

    public CreateBankDepositComercialEcuadorUseCase(ISapGateway gateway, ILogger<CreateBankDepositComercialEcuadorUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<BankDepositResponse> ExecuteAsync(CreateBankDepositRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating Bank Deposit (Comercial Ecuador) — CodigoAlmacen={CodigoAlmacen}, NumeroComprobante={NumeroComprobante}, Monto={Monto}",
            request.CodigoAlmacen, request.NumeroComprobante, request.MontoDepositado);

        var result = await _gateway.CreateBankDepositForComercialEcuadorAsync(request, cancellationToken);

        _logger.LogInformation("Bank Deposit created (Comercial Ecuador): DocEntry={DocEntry}", result.DocEntry);

        return new BankDepositResponse(result.DocEntry, result.Mensaje);
    }
}
