using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Domain.Enums;

namespace Middleware.Application.UseCases.Deposits;

public sealed class CreateDepositUseCase : IUseCase<CreateDepositRequest, DepositResponse>
{
    private readonly ISapGateway _gateway;
    private readonly DepositSettings _settings;
    private readonly ILogger<CreateDepositUseCase> _logger;

    public CreateDepositUseCase(
        ISapGateway gateway,
        IOptions<DepositSettings> settings,
        ILogger<CreateDepositUseCase> logger)
    {
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<DepositResponse> ExecuteAsync(CreateDepositRequest request, CancellationToken cancellationToken = default)
    {
        var companyDb = request.Environment == CompanyEnvironment.Test
            ? _settings.TestCompanyDb
            : _settings.ProductionCompanyDb;

        _logger.LogInformation(
            "Creating Deposit type={Type} env={Environment} companyDb={CompanyDb} lines={LineCount}",
            request.Type, request.Environment, companyDb, request.Lines.Count);

        var result = await _gateway.CreateDepositAsync(request, companyDb, cancellationToken);

        _logger.LogInformation("Deposit created: DocEntry={DocEntry}, DocNum={DocNum}", result.DocEntry, result.DocNum);

        return new DepositResponse(result.DocEntry, result.DocNum);
    }
}

public sealed class DepositSettings
{
    public const string SectionName = "Deposit";
    public string ProductionCompanyDb { get; init; } = string.Empty;
    public string TestCompanyDb { get; init; } = string.Empty;
}
