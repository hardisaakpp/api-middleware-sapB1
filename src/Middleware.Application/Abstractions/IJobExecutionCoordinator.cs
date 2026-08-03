namespace Middleware.Application.Abstractions;

public interface IJobExecutionCoordinator
{
    Task<JobExecutionOutcome<TResponse>> ExecuteAsync<TResponse>(
        string requestType,
        string endpoint,
        string requestPayloadJson,
        Func<IServiceProvider, CancellationToken, Task<TResponse>> attempt,
        int successStatusCode,
        CancellationToken cancellationToken = default);
}

public abstract record JobExecutionOutcome<TResponse>; // NOSONAR: TResponse is the discriminator shared by JobCompleted/JobQueued below

public sealed record JobCompleted<TResponse>(TResponse Response) : JobExecutionOutcome<TResponse>;

public sealed record JobQueued<TResponse>(Guid JobId) : JobExecutionOutcome<TResponse>;
