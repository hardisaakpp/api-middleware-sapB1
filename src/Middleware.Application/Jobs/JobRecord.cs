namespace Middleware.Application.Jobs;

public sealed record JobRecord(
    Guid Id,
    string Endpoint,
    string Status,
    int? HttpStatusCode,
    string? ResultPayloadJson,
    string? ErrorMessage,
    int Attempts,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);
