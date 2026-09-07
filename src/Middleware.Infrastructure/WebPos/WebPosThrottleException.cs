namespace Middleware.Infrastructure.WebPos;

public sealed class WebPosThrottleException : Exception
{
    public WebPosThrottleException(string message) : base(message) { }
}
