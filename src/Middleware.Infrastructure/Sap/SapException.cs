using System.Net;

namespace Middleware.Infrastructure.Sap;

public sealed class SapException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string SapError { get; }

    public SapException(HttpStatusCode statusCode, string sapError)
        : base($"SAP Service Layer returned HTTP {(int)statusCode}: {sapError}")
    {
        StatusCode = statusCode;
        SapError = sapError;
    }
}
