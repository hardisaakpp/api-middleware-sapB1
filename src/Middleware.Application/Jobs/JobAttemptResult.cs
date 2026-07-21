namespace Middleware.Application.Jobs;

public sealed record JobAttemptResult(int HttpStatusCode, string ResponsePayloadJson);
