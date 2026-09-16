namespace Middleware.Infrastructure.Configuration;

public sealed class SapSettings
{
    public const string SectionName = "Sap";

    public string BaseUrl { get; init; } = string.Empty;
    public string CompanyDb { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxConcurrentSapCalls { get; init; } = 5;
    public int ThrottleWaitSeconds { get; init; } = 8;
    public int ItemGroupsCacheMinutes { get; init; } = 360;
    public string? TestCompanyDb { get; init; }

    /// <summary>Password for UserName against TestCompanyDb. SAP B1 users are scoped per CompanyDB, so this can differ from Password.</summary>
    public string? TestPassword { get; init; }

    /// <summary>CompanyDB for the Comercial Ecuador SAP B1 company — used by Bank Deposits (ODPS) posted via POST /api/Deposit/CE.</summary>
    public string? ComercialEcuadorCompanyDb { get; init; }

    /// <summary>Password for UserName against ComercialEcuadorCompanyDb. SAP B1 users are scoped per CompanyDB, so this can differ from Password.</summary>
    public string? ComercialEcuadorPassword { get; init; }
}
