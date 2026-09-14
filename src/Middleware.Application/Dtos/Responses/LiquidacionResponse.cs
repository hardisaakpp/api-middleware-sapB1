namespace Middleware.Application.Dtos.Responses;

public sealed record LiquidacionResponse(int DocEntry, int DocNum, bool Recovered = false);
