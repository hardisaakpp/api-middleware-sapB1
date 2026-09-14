namespace Middleware.Application.Dtos.Responses;

public sealed record CreatePurchaseOrderResponse(int DocEntry, int DocNum, bool Recovered = false);
