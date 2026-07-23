using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Liquidaciones;

public sealed class CreateLiquidacionUseCase : IUseCase<CreateLiquidacionRequest, LiquidacionResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreateLiquidacionUseCase> _logger;

    public CreateLiquidacionUseCase(ISapGateway gateway, ILogger<CreateLiquidacionUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<LiquidacionResponse> ExecuteAsync(CreateLiquidacionRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating Liquidacion for CardCode {CardCode} with {LineCount} lines",
            request.CardCode, request.DocumentLines?.Count ?? 0);

        var result = await _gateway.CreateLiquidacionAsync(request, cancellationToken);

        _logger.LogInformation("Liquidacion created: DocEntry={DocEntry}, DocNum={DocNum}", result.DocEntry, result.DocNum);

        return new LiquidacionResponse(result.DocEntry, result.DocNum);
    }
}
