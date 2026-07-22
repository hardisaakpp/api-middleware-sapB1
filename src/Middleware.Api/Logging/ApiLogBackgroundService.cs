using Middleware.Application.Abstractions;

namespace Middleware.Api.Logging;

/// <summary>
/// Drena la <see cref="ApiLogQueue"/> y persiste cada entrada fuera del camino crítico
/// del request. <see cref="IApiLogRepository"/> es scoped, así que se crea un scope de DI
/// por inserción. Un fallo de una entrada se registra y no detiene el bucle.
/// </summary>
public sealed class ApiLogBackgroundService : BackgroundService
{
    private readonly ApiLogQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApiLogBackgroundService> _logger;

    public ApiLogBackgroundService(
        ApiLogQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ApiLogBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var entry in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IApiLogRepository>();
                    await repo.InsertAsync(entry, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Inserción de auditoría en background falló para {Endpoint}", entry.Endpoint);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown normal: las entradas pendientes en cola se descartan (best-effort).
        }
    }
}
