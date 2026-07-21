using Middleware.Application.Jobs;

namespace Middleware.Application.Abstractions;

public interface IJobHandler
{
    string RequestType { get; }
    Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default);
}
