using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.BankAccounts;

public sealed class GetBankAccountsUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetBankAccountsUseCase> _logger;

    public GetBankAccountsUseCase(ISapGateway gateway, ILogger<GetBankAccountsUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ChartOfAccountsResponse> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting Mabel Trading bank account chart of accounts (1.01.*) from SAP");
        var accounts = await _gateway.GetBankAccountsAsync(cancellationToken);
        return new ChartOfAccountsResponse(accounts);
    }
}
