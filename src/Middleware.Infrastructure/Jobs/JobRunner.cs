using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Jobs;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Jobs;

internal sealed class JobRunner
{
    private readonly IJobQueueRepository _repository;
    private readonly JobQueueSettings _settings;
    private readonly ILogger<JobRunner> _logger;

    public JobRunner(IJobQueueRepository repository, IOptions<JobQueueSettings> settings, ILogger<JobRunner> logger)
    {
        _repository = repository;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task RetryUntilResolvedAsync<TResponse>(
        Guid jobId, int attemptsSoFar, Func<Task<TResponse>> attempt, int successStatusCode, CancellationToken ct = default)
        => RunRetryLoopAsync(jobId, attemptsSoFar, async () =>
        {
            var response = await attempt();
            return new JobAttemptResult(successStatusCode, JsonSerializer.Serialize(response, JobJsonOptions.Default));
        }, ct);

    public Task RetryUntilResolvedAsync(Guid jobId, int attemptsSoFar, Func<Task<JobAttemptResult>> attempt, CancellationToken ct = default)
        => RunRetryLoopAsync(jobId, attemptsSoFar, attempt, ct);

    private async Task RunRetryLoopAsync(Guid jobId, int attemptsSoFar, Func<Task<JobAttemptResult>> attempt, CancellationToken ct)
    {
        var attempts = attemptsSoFar;
        string? lastTransientError = null;

        while (attempts < _settings.MaxRetryAttempts)
        {
            var claimed = await _repository.MarkProcessingAsync(jobId, ct);
            if (!claimed)
            {
                return;
            }

            try
            {
                var result = await attempt();
                await _repository.MarkCompletedAsync(jobId, result.HttpStatusCode, result.ResponsePayloadJson, ct);
                return;
            }
            catch (Exception ex) when (JobExceptionClassifier.IsTransient(ex))
            {
                attempts++;
                lastTransientError = ex.Message;
                var backoffSeconds = _settings.RetryBackoffSeconds[Math.Min(attempts - 1, _settings.RetryBackoffSeconds.Length - 1)];
                var nextRetryAt = DateTime.Now.AddSeconds(backoffSeconds);

                _logger.LogWarning(ex, "Job {JobId} failed transiently (attempt {Attempts}/{Max}), retrying in {Backoff}s",
                    jobId, attempts, _settings.MaxRetryAttempts, backoffSeconds);

                await _repository.ScheduleRetryAsync(jobId, attempts, nextRetryAt, ct);
                await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed permanently", jobId);
                await _repository.MarkErrorAsync(jobId, ex.Message, JobExceptionClassifier.MapToHttpStatus(ex), ct);
                return;
            }
        }

        _logger.LogError("Job {JobId} exhausted {Max} retry attempts. Last error: {LastError}",
            jobId, _settings.MaxRetryAttempts, lastTransientError);

        // El motivo real importa para soporte: un timeout de SAP ("The operation didn't complete
        // within the allowed timeout") significa que el documento PUDO haberse creado igual y hay
        // que verificarlo en SAP, cosa que "saturado" a secas no dejaba ver.
        var message = string.IsNullOrWhiteSpace(lastTransientError)
            ? "SAP saturado: se agotaron los reintentos."
            : $"SAP saturado: se agotaron los reintentos. Último error: {lastTransientError}";
        await _repository.MarkErrorAsync(jobId, message, (int)HttpStatusCode.ServiceUnavailable, ct);
    }
}
