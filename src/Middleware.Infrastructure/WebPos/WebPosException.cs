namespace Middleware.Infrastructure.WebPos;

/// <summary>Transport-level failure calling WEBPOS (non-2xx that isn't a parseable business answer).</summary>
public sealed class WebPosException : Exception
{
    public System.Net.HttpStatusCode StatusCode { get; }
    public string RawResponse { get; }

    public WebPosException(System.Net.HttpStatusCode statusCode, string rawResponse)
        : base($"WEBPOS returned HTTP {(int)statusCode}: {rawResponse}")
    {
        StatusCode = statusCode;
        RawResponse = rawResponse;
    }
}
