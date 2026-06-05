namespace Middleware.Infrastructure.Sap;

public sealed class SapResponseException : Exception
{
    public SapResponseException(string message) : base(message) { }
}
