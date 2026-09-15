namespace Middleware.Application.Dtos.Responses;

public sealed record HasItemMovementsResponse(
    string ItemCode,
    bool HasMovements
);
