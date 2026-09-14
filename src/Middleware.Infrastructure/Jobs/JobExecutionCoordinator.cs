using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Jobs;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Jobs;

internal sealed class JobExecutionCoordinator : IJobExecutionCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobQueueRepository _repository;
    private readonly JobRunner _jobRunner;
    private readonly JobQueueSettings _settings;
    private readonly ILogger<JobExecutionCoordinator> _logger;

    public JobExecutionCoordinator(
        IServiceScopeFactory scopeFactory,
        IJobQueueRepository repository,
        JobRunner jobRunner,
        IOptions<JobQueueSettings> settings,
        ILogger<JobExecutionCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _repository = repository;
        _jobRunner = jobRunner;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<JobExecutionOutcome<TResponse>> ExecuteAsync<TResponse>(
        string requestType,
        string endpoint,
        string requestPayloadJson,
        Func<IServiceProvider, CancellationToken, Task<TResponse>> attempt,
        int successStatusCode,
        CancellationToken cancellationToken = default)
    {
        // Si ya hay un job encolado/procesando para exactamente la misma request (mismo
        // requestType+endpoint+payload), nos enganchamos a ese en vez de lanzar otro intento
        // contra SAP. Sin esto, cada reintento manual del usuario (p.ej. recargando la busqueda
        // varias veces mientras SAP esta lento/caido) dispara su propia cadena independiente de
        // intentos contra SAP, sumando carga justo cuando SAP ya esta saturado.
        // La deduplicación es una optimización: si la consulta a la cola falla (SQL caído o no
        // configurado) se sigue con el intento normal contra SAP en vez de responder 500.
        var activeJobId = await TryFindActiveJobIdAsync(requestType, endpoint, requestPayloadJson, cancellationToken);
        if (activeJobId is not null)
        {
            _logger.LogInformation(
                "Job {JobId} ya en curso para {RequestType} {Endpoint}; se reutiliza en vez de reintentar contra SAP",
                activeJobId, requestType, endpoint);
            return new JobQueued<TResponse>(activeJobId.Value);
        }

        Task<TResponse> RunOnceAsync() => RunInFreshScopeAsync(attempt);

        var firstAttempt = RunOnceAsync();
        var delayTask = Task.Delay(TimeSpan.FromSeconds(_settings.QueueAfterSeconds), cancellationToken);

        await Task.WhenAny(firstAttempt, delayTask);

        if (firstAttempt.IsCompletedSuccessfully)
        {
            return new JobCompleted<TResponse>(firstAttempt.Result);
        }

        if (firstAttempt.IsFaulted && !JobExceptionClassifier.IsTransient(firstAttempt.Exception!.GetBaseException()))
        {
            // A genuine business/validation error surfaced fast — behave exactly like today, no queueing.
            return await Task.FromException<JobExecutionOutcome<TResponse>>(firstAttempt.Exception!.GetBaseException());
        }

        // Either still running past the deadline, or it already failed transiently (SAP saturated) —
        // both cases get absorbed into the durable queue instead of surfacing an error to the caller.
        var jobId = Guid.NewGuid();
        await _repository.InsertQueuedAsync(jobId, endpoint, requestType, requestPayloadJson, CancellationToken.None);

        _logger.LogInformation("Job {JobId} ({RequestType}) queued for {Endpoint}", jobId, requestType, endpoint);

        if (firstAttempt.IsFaulted)
        {
            _ = _jobRunner.RetryUntilResolvedAsync(jobId, attemptsSoFar: 0, RunOnceAsync, successStatusCode, CancellationToken.None);
        }
        else
        {
            _ = HandleFirstAttemptOutcomeAsync(jobId, firstAttempt, successStatusCode, RunOnceAsync);
        }

        return new JobQueued<TResponse>(jobId);
    }

    private async Task<Guid?> TryFindActiveJobIdAsync(string requestType, string endpoint, string requestPayloadJson, CancellationToken ct)
    {
        try
        {
            return await _repository.FindActiveIdAsync(requestType, endpoint, requestPayloadJson, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No se pudo consultar la cola por un job en curso para {RequestType} {Endpoint}; se continúa sin deduplicar", // NOSONAR
                requestType, endpoint);
            return null;
        }
    }

    private async Task<TResponse> RunInFreshScopeAsync<TResponse>(Func<IServiceProvider, CancellationToken, Task<TResponse>> attempt)
    {
        using var scope = _scopeFactory.CreateScope();
        return await attempt(scope.ServiceProvider, CancellationToken.None);
    }

    private async Task HandleFirstAttemptOutcomeAsync<TResponse>(
        Guid jobId, Task<TResponse> firstAttempt, int successStatusCode, Func<Task<TResponse>> retryAttempt)
    {
        try
        {
            var response = await firstAttempt;
            await _repository.MarkCompletedAsync(jobId, successStatusCode, JsonSerializer.Serialize(response, JobJsonOptions.Default), CancellationToken.None);
        }
        catch (Exception ex) when (JobExceptionClassifier.IsTransient(ex))
        {
            await _jobRunner.RetryUntilResolvedAsync(jobId, attemptsSoFar: 0, retryAttempt, successStatusCode, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed permanently", jobId);
            await _repository.MarkErrorAsync(jobId, ex.Message, JobExceptionClassifier.MapToHttpStatus(ex), CancellationToken.None);
        }
    }
}
