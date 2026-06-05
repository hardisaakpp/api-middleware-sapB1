using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace Middleware.IntegrationTests.StressTests;

/// <summary>
/// Stress test that measures SAP Service Layer capacity at increasing concurrency levels.
/// Run intentionally with: dotnet test --filter "Category=stress"
/// Never runs in CI — it hits real SAP.
///
/// What it does:
///   For each concurrency level (1 → 2 → 5 → 10 → 20):
///     - Raises MaxConcurrentSapCalls to match the level (removes throttle cap for that phase)
///     - Fires 30 requests concurrently through the real middleware stack
///     - Measures min / p50 / p95 / p99 / max response time and error rate
///     - Stops automatically if the error rate exceeds 20 %
///
/// Interpretation:
///   The last phase where p95 < 5 s and errors = 0 % is the safe MaxConcurrentSapCalls
///   value to set in appsettings.json for production.
/// </summary>
[Trait("Category", "stress")]
public sealed class SapCapacityTest
{
    private static readonly int[] ConcurrencyLevels = [1, 2, 5, 10, 20];
    private const int RequestsPerPhase = 30;
    private const double ErrorRateAbortThreshold = 0.20; // stop if >20% errors

    private readonly ITestOutputHelper _output;

    public SapCapacityTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task RampUp_FindsSapConcurrencyLimit()
    {
        _output.WriteLine("SAP Capacity Stress Test");
        _output.WriteLine($"Requests per phase : {RequestsPerPhase}");
        _output.WriteLine($"Concurrency levels : {string.Join(" → ", ConcurrencyLevels)}");
        _output.WriteLine(new string('─', 72));
        _output.WriteLine($"{"Level",6} {"OK",6} {"Err",6} {"ErrRate",8} {"min",7} {"p50",7} {"p95",7} {"p99",7} {"max",7}");
        _output.WriteLine(new string('─', 72));

        foreach (var level in ConcurrencyLevels)
        {
            var result = await RunPhaseAsync(level);
            PrintRow(level, result);

            if (result.ErrorRate > ErrorRateAbortThreshold)
            {
                _output.WriteLine(new string('─', 72));
                _output.WriteLine($"Aborting: error rate {result.ErrorRate:P0} at concurrency {level} exceeds threshold.");
                _output.WriteLine("SAP limit is around the previous phase.");
                return;
            }
        }

        _output.WriteLine(new string('─', 72));
        _output.WriteLine("All phases completed without exceeding error threshold.");
        _output.WriteLine($"Consider testing higher levels if SAP can handle more.");
    }

    private async Task<PhaseResult> RunPhaseAsync(int concurrency)
    {
        // Each phase gets its own factory so MaxConcurrentSapCalls matches the level.
        // This removes the throttle cap for the phase so we're measuring SAP, not the throttle.
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.ConfigureAppConfiguration(cfg =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sap:MaxConcurrentSapCalls"] = concurrency.ToString()
                })));

        var client = factory.CreateClient();

        // Warmup: one request to establish the SAP session before the timed phase
        await client.GetAsync("/api/v1/purchase-orders/1");

        var timings = new List<long>(RequestsPerPhase);
        var errors = 0;
        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(1, RequestsPerPhase).Select(async i =>
        {
            var reqSw = Stopwatch.StartNew();
            try
            {
                var response = await client.GetAsync($"/api/v1/purchase-orders/{i}");
                reqSw.Stop();

                if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.NotFound))
                    Interlocked.Increment(ref errors);
            }
            catch
            {
                reqSw.Stop();
                Interlocked.Increment(ref errors);
            }
            finally
            {
                lock (timings) timings.Add(reqSw.ElapsedMilliseconds);
            }
        });

        await Task.WhenAll(tasks);
        sw.Stop();

        timings.Sort();
        return new PhaseResult(
            Ok: RequestsPerPhase - errors,
            Errors: errors,
            TotalMs: sw.ElapsedMilliseconds,
            Timings: timings);
    }

    private void PrintRow(int level, PhaseResult r)
    {
        _output.WriteLine(
            $"{level,6} {r.Ok,6} {r.Errors,6} {r.ErrorRate,8:P0} " +
            $"{r.Min,6}ms {r.P50,6}ms {r.P95,6}ms {r.P99,6}ms {r.Max,6}ms");
    }

    private sealed record PhaseResult(int Ok, int Errors, long TotalMs, List<long> Timings)
    {
        public double ErrorRate => (double)Errors / (Ok + Errors);
        public long Min => Timings.Count > 0 ? Timings[0] : 0;
        public long Max => Timings.Count > 0 ? Timings[^1] : 0;
        public long P50 => Percentile(50);
        public long P95 => Percentile(95);
        public long P99 => Percentile(99);

        private long Percentile(int p)
        {
            if (Timings.Count == 0) return 0;
            var idx = (int)Math.Ceiling(p / 100.0 * Timings.Count) - 1;
            return Timings[Math.Clamp(idx, 0, Timings.Count - 1)];
        }
    }
}
