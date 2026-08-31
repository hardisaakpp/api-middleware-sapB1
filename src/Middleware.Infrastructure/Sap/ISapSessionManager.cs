namespace Middleware.Infrastructure.Sap;

public interface ISapSessionManager
{
    public Task<string> GetSessionTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as the parameterless overload, but logs in against a specific CompanyDB/password pair
    /// (e.g. Sap:TestCompanyDb / Sap:TestPassword) instead of Sap:CompanyDb / Sap:Password.
    /// SAP B1 users are scoped per CompanyDB, so the same UserName can have a different password there.
    /// </summary>
    public Task<string> GetSessionTokenAsync(string companyDb, string password, CancellationToken cancellationToken = default);
    public Task InvalidateSessionAsync(CancellationToken cancellationToken = default);
    public Task InvalidateSessionAsync(string companyDb, CancellationToken cancellationToken = default);
}
