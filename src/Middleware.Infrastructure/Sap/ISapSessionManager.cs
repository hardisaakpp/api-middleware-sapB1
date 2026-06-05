namespace Middleware.Infrastructure.Sap;

public interface ISapSessionManager
{
    Task<string> GetSessionTokenAsync(CancellationToken cancellationToken = default);
    Task InvalidateSessionAsync(CancellationToken cancellationToken = default);
}
