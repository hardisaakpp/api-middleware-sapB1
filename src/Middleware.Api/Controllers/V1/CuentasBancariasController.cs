using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.BankAccounts;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/cuentas-bancarias")]
public sealed class CuentasBancariasController : ControllerBase
{
    private readonly GetBankAccountsUseCase _getUseCase;

    public CuentasBancariasController(GetBankAccountsUseCase getUseCase)
    {
        _getUseCase = getUseCase;
    }

    /// <summary>Returns Mabel Trading's active bank accounts (ChartOfAccounts 1.01.*) from SAP.</summary>
    [HttpGet("mabel")]
    [ProducesResponseType(typeof(ChartOfAccountsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChartOfAccountsResponse>> GetMabel(CancellationToken cancellationToken)
    {
        var result = await _getUseCase.ExecuteAsync(cancellationToken);
        return Ok(result);
    }
}
