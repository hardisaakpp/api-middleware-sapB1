namespace Middleware.Infrastructure.WebPos;

public interface IWebPosClient
{
    /// <summary>
    /// Sends a request to WEBPOS and returns the raw JSON body for both 200 and 400 responses —
    /// WEBPOS uses either status to carry a business answer (<c>procesoCorrecto</c> in the body
    /// decides success, not the HTTP status). Any other status (401, 404, 5xx, timeouts) throws
    /// <see cref="WebPosException"/> instead of returning, so the caller can tell a genuine
    /// transport/availability failure apart from a parseable business answer.
    /// </summary>
    Task<string> SendAsync(HttpMethod method, string relativeUrl, string body, string token, CancellationToken ct = default);
}
