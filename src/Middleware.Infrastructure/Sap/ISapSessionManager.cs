namespace Middleware.Infrastructure.Sap;

public interface ISapSessionManager
{
    public Task<string> GetSessionTokenAsync(CancellationToken cancellationToken = default);
    public Task InvalidateSessionAsync(CancellationToken cancellationToken = default);
}
