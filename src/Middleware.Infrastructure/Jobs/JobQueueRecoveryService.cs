using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Jobs;

namespace Middleware.Infrastructure.Jobs;

internal sealed class JobQueueRecoveryService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobQueueRecoveryService> _logger;

    public JobQueueRecoveryService(IServiceScopeFactory scopeFactory, ILogger<JobQueueRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<OrphanedJob> orphaned;
            using (var scope = _scopeFactory.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<IJobQueueRepository>();
                orphaned = await repository.GetOrphanedAsync(cancellationToken);
            }

            if (orphaned.Count == 0)
            {
                return;
            }

            _logger.LogWarning("Recovering {Count} orphaned job(s) from a previous run", orphaned.Count);

            foreach (var job in orphaned)
            {
                _ = ResumeJobAsync(job);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job queue recovery failed — orphaned jobs (if any) will remain queued until the next restart"); // NOSONAR
        }
    }

    private async Task ResumeJobAsync(OrphanedJob job)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRunner = scope.ServiceProvider.GetRequiredService<JobRunner>();

        async Task<JobAttemptResult> AttemptAsync()
        {
            using var attemptScope = _scopeFactory.CreateScope();
            var handler = attemptScope.ServiceProvider.GetServices<IJobHandler>()
                .FirstOrDefault(h => h.RequestType == job.RequestType);

            if (handler is null)
            {
                throw new InvalidOperationException($"No IJobHandler registered for RequestType '{job.RequestType}'.");
            }

            return await handler.ExecuteAsync(job.RequestPayloadJson, CancellationToken.None);
        }

        await jobRunner.RetryUntilResolvedAsync(job.Id, job.Attempts, AttemptAsync, CancellationToken.None);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
