using System.Collections.Concurrent;
using Middleware.Infrastructure.Sap;

namespace Middleware.IntegrationTests.Infrastructure;

/// <summary>
/// Fake SAP HTTP client whose responses are scripted per call by the test (return a JSON body or
/// throw), recording every call so tests can assert exactly what reached "SAP" and in which order.
/// Keeps the real SapGateway in place — that's the code under test.
/// </summary>
internal sealed class ScriptedSapServiceLayerClient : ISapServiceLayerClient
{
    internal sealed record Call(HttpMethod Method, string Url, string? Body);

    private readonly ConcurrentQueue<Call> _calls = new();

    public IReadOnlyList<Call> Calls => _calls.ToList();

    public IReadOnlyList<Call> Posts => Calls.Where(c => c.Method == HttpMethod.Post).ToList();

    public IReadOnlyList<Call> Gets => Calls.Where(c => c.Method == HttpMethod.Get).ToList();

    /// <summary>Decides the outcome of each call. Default: an empty JSON object.</summary>
    public Func<Call, Task<string>> Handler { get; set; } = _ => Task.FromResult("{}");

    public Task<string> SendAsync(
        HttpMethod method,
        string relativeUrl,
        string? body,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        var call = new Call(method, relativeUrl, body);
        _calls.Enqueue(call);
        return Handler(call);
    }
}
