namespace Middleware.Application.Abstractions;

public record ApiLogEntry(
    string Endpoint,
    string? Request,
    string? Response,
    string Usuario,
    int StatusCode,
    int DurationMs);

public interface IApiLogRepository
{
    Task InsertAsync(ApiLogEntry entry, CancellationToken ct = default);
}
