using System.Net;
using System.Collections.Concurrent;
using Middleware.Infrastructure.WebPos;

namespace Middleware.IntegrationTests.Infrastructure;

/// <summary>
/// Fake WEBPOS HTTP client for integration tests — lets tests exercise the real WebPosGateway
/// body-parsing logic (procesoCorrecto decides success, not the HTTP status) without a real
/// WEBPOS server. Queue responses via <see cref="NextResponses"/>; each call dequeues the next one.
/// </summary>
internal sealed class FakeWebPosClient : IWebPosClient
{
    public ConcurrentQueue<(string Body, HttpStatusCode StatusCode)> NextResponses { get; } = new();
    public int CallCount { get; private set; }

    public Task<string> SendAsync(HttpMethod method, string relativeUrl, string body, string token, CancellationToken ct = default)
    {
        CallCount++;

        if (!NextResponses.TryDequeue(out var response))
        {
            throw new InvalidOperationException("FakeWebPosClient has no queued response for this call.");
        }

        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest)
        {
            return Task.FromResult(response.Body);
        }

        throw new WebPosException(response.StatusCode, response.Body);
    }
}
