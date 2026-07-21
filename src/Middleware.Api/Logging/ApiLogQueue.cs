using System.Threading.Channels;
using Middleware.Application.Abstractions;

namespace Middleware.Api.Logging;

/// <summary>
/// Cola en memoria acotada que desacopla la persistencia del log de auditoría del
/// camino crítico del request. El hilo del request encola sin bloquear; el
/// <see cref="ApiLogBackgroundService"/> drena y persiste en SQL. Si la base no da
/// abasto se descartan las entradas más viejas, de modo que la API nunca se bloquea
/// ni consume memoria sin límite cuando SQL está lento o caído.
/// </summary>
public sealed class ApiLogQueue
{
    private const int Capacity = 1000;

    private readonly Channel<ApiLogEntry> _channel;
    private readonly ILogger<ApiLogQueue> _logger;
    private int _dropped;

    public ApiLogQueue(ILogger<ApiLogQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<ApiLogEntry>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            itemDropped: _ =>
            {
                var n = Interlocked.Increment(ref _dropped);
                if (n % 100 == 1)
                {
                    _logger.LogWarning(
                        "Cola de auditoría saturada; {Count} entradas descartadas (¿SQL lento o caído?)", n);
                }
            });
    }

    /// <summary>Encola sin bloquear. Con DropOldest el write siempre es aceptado.</summary>
    public void Enqueue(ApiLogEntry entry) => _channel.Writer.TryWrite(entry);

    public ChannelReader<ApiLogEntry> Reader => _channel.Reader;
}
