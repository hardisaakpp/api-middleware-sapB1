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
    public string? TestCompanyDb { get; init; }
}
