namespace Middleware.Infrastructure.Sap;

public sealed class SapThrottleException : Exception
{
    public SapThrottleException(string message) : base(message) { }
}
