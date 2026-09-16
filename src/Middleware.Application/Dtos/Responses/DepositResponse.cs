namespace Middleware.Application.Dtos.Responses;

public sealed record DepositResponse(int DocEntry, int DocNum, bool Recovered = false);
