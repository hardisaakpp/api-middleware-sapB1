using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Warehouses;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/warehouses")]
public sealed class WarehousesController : ControllerBase
{
    private readonly GetWarehousesUseCase _getUseCase;
    private readonly ValidateWarehousesUseCase _validateUseCase;
    private readonly IValidator<ValidateWarehousesRequest> _requestValidator;

    public WarehousesController(
        GetWarehousesUseCase getUseCase,
        ValidateWarehousesUseCase validateUseCase,
        IValidator<ValidateWarehousesRequest> requestValidator)
    {
        _getUseCase = getUseCase;
        _validateUseCase = validateUseCase;
        _requestValidator = requestValidator;
    }

    /// <summary>Returns the full warehouse catalog from SAP.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(WarehousesListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WarehousesListResponse>> Get(CancellationToken cancellationToken)
    {
        var result = await _getUseCase.ExecuteAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Validates a list of warehouse codes in a single batch. Always returns 200 when the
    /// batch could be processed; per-element problems go in each element's errorType.
    /// Returns 503 only when nothing could be queried.
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(ValidateWarehousesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ValidateWarehousesResponse>> Validate(
        [FromBody] ValidateWarehousesRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _requestValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }
            return ValidationProblem(ModelState);
        }

        var result = await _validateUseCase.ExecuteAsync(request, cancellationToken);

        // 503 only when Service Layer was unreachable for every code in the batch
        if (result.Warehouses.Count > 0 &&
            result.Warehouses.All(w => w.ErrorType == ValidationErrorTypes.ConnectionError))
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "SAP Service Layer unreachable",
                detail: "None of the warehouse codes could be validated against SAP Service Layer.");
        }

        return Ok(result);
    }
}
