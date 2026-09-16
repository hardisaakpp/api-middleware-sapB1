using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.BankDeposits;

namespace Middleware.Api.Controllers;

/// <summary>
/// Depósitos bancarios de efectivo (SAP B1 ODPS) consumidos por api-tesoreria vía
/// ISapMiddlewareClient. Rutas fijadas por ese contrato ya existente — sin versión de API
/// ni header de tenant: la empresa se resuelve por ruta (/api/Deposit vs /api/Deposit/CE).
/// No confundir con V1/DepositsController, que atiende Entradas de Mercancías (OIGN).
/// </summary>
[ApiController]
[Route("api/Deposit")]
public sealed class BankDepositController : ControllerBase
{
    private readonly CreateBankDepositUseCase _mabelTradingUseCase;
    private readonly CreateBankDepositComercialEcuadorUseCase _comercialEcuadorUseCase;
    private readonly IValidator<CreateBankDepositRequest> _validator;

    public BankDepositController(
        CreateBankDepositUseCase mabelTradingUseCase,
        CreateBankDepositComercialEcuadorUseCase comercialEcuadorUseCase,
        IValidator<CreateBankDepositRequest> validator)
    {
        _mabelTradingUseCase = mabelTradingUseCase;
        _comercialEcuadorUseCase = comercialEcuadorUseCase;
        _validator = validator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(BankDepositResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public Task<ActionResult<BankDepositResponse>> Create(
        [FromBody] CreateBankDepositRequest request, CancellationToken cancellationToken)
        => CreateInternalAsync(request, _mabelTradingUseCase, cancellationToken);

    [HttpPost("CE")]
    [ProducesResponseType(typeof(BankDepositResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public Task<ActionResult<BankDepositResponse>> CreateComercialEcuador(
        [FromBody] CreateBankDepositRequest request, CancellationToken cancellationToken)
        => CreateInternalAsync(request, _comercialEcuadorUseCase, cancellationToken);

    private async Task<ActionResult<BankDepositResponse>> CreateInternalAsync(
        CreateBankDepositRequest request,
        IUseCase<CreateBankDepositRequest, BankDepositResponse> useCase,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }
            return ValidationProblem(ModelState);
        }

        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }
}
