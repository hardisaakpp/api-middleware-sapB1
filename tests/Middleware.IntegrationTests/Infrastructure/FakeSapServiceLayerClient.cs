using Middleware.Infrastructure.Sap;

namespace Middleware.IntegrationTests.Infrastructure;

/// <summary>
/// Fake SAP HTTP client with configurable delay and thread-safe concurrency tracking.
/// Used to verify that SapThrottle limits peak concurrent calls without hitting real SAP.
/// </summary>
internal sealed class FakeSapServiceLayerClient : ISapServiceLayerClient
{
    private readonly TimeSpan _delay;
    private int _activeCalls;
    private int _peakCalls;
    private int _totalCalls;

    public FakeSapServiceLayerClient(TimeSpan delay) => _delay = delay;

    public int PeakConcurrentCalls => Volatile.Read(ref _peakCalls);
    public int TotalCalls => Volatile.Read(ref _totalCalls);

    public async Task<string> SendAsync(
        HttpMethod method,
        string relativeUrl,
        string? body,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalCalls);

        // Track concurrent calls using a CAS loop for the peak
        var current = Interlocked.Increment(ref _activeCalls);
        int peak;
        do { peak = _peakCalls; }
        while (current > peak && Interlocked.CompareExchange(ref _peakCalls, current, peak) != peak);

        try
        {
            await Task.Delay(_delay, cancellationToken);

            return """
                {
                    "DocEntry": 1,
                    "DocNum": 100,
                    "CardCode": "C001",
                    "DocDate": "2024-01-01",
                    "DocumentStatus": "bost_Open",
                    "DocumentLines": []
                }
                """;
        }
        finally
        {
            Interlocked.Decrement(ref _activeCalls);
        }
    }
}
