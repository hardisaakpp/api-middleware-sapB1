using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Validations;

// The Application layer does not reference Polly/Infrastructure, so transport-level
// failures are recognised structurally (type or type name) rather than by reference.
internal static class ValidationErrorClassifier
{
    public static (string Type, string? Message) Classify(Exception ex)
    {
        var name = ex.GetType().Name;
        var isConnection =
            ex is OperationCanceledException or TimeoutException or HttpRequestException ||
            name.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Circuit", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Throttle", StringComparison.OrdinalIgnoreCase);

        return isConnection
            ? (ValidationErrorTypes.ConnectionError, null)
            : (ValidationErrorTypes.Error, ex.Message);
    }
}
