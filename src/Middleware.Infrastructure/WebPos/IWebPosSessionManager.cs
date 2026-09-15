namespace Middleware.Infrastructure.WebPos;

public interface IWebPosSessionManager
{
    Task<string> GetTokenAsync(CancellationToken ct = default);
    Task InvalidateAsync(CancellationToken ct = default);
}
