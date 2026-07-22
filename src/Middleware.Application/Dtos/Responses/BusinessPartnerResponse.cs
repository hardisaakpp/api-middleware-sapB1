namespace Middleware.Application.Dtos.Responses;

public sealed record BusinessPartnerResponse(
    string CardCode,
    string CardName,
    string CardType,
    string? Currency,
    string? Phone1,
    string? EmailAddress,
    bool Valid,
    bool Frozen
);
