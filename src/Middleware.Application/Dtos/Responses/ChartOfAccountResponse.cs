namespace Middleware.Application.Dtos.Responses;

public sealed record ChartOfAccountsResponse(
    IReadOnlyList<ChartOfAccountResponse> Accounts
);

public sealed record ChartOfAccountResponse(
    string Code,
    string Name,
    string FormatCode,
    bool ActiveAccount
);
