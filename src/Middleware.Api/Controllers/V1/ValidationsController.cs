using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Validations;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/validations")]
public sealed class ValidationsController : ControllerBase
{
    private readonly ValidatePurchaseOrderUseCase _validateUseCase;
    private readonly IValidator<ValidatePurchaseOrderRequest> _requestValidator;

    public ValidationsController(
        ValidatePurchaseOrderUseCase validateUseCase,
        IValidator<ValidatePurchaseOrderRequest> requestValidator)
    {
        _validateUseCase = validateUseCase;
        _requestValidator = requestValidator;
    }

    /// <summary>
    /// Validates a full purchase order (supplier + item/warehouse lines) in a single batch.
    /// Always returns 200 when the batch could be processed; per-element problems are
    /// reported in each element's errorType. Returns 503 only when nothing could be queried.
    /// </summary>
    [HttpPost("purchase-order")]
    [ProducesResponseType(typeof(ValidatePurchaseOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ValidatePurchaseOrderResponse>> ValidatePurchaseOrder(
        [FromBody] ValidatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _requestValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        var result = await _validateUseCase.ExecuteAsync(request, cancellationToken);

        // 503 only when Service Layer was unreachable for every element in the batch
        var allItemsUnreachable = result.Items.Count > 0 &&
            result.Items.All(i => i.ErrorType == ValidationErrorTypes.ConnectionError);
        var partnerUnreachableOrAbsent = result.BusinessPartner is null ||
            result.BusinessPartner.ErrorType == ValidationErrorTypes.ConnectionError;

        if (allItemsUnreachable && partnerUnreachableOrAbsent)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "SAP Service Layer unreachable",
                detail: "None of the batch elements could be validated against SAP Service Layer.");
        }

        return Ok(result);
    }
}
