namespace Middleware.Application.Dtos.Responses;

public sealed record PriceListsResponse(
    IReadOnlyList<PriceListResponse> PriceLists
);

public sealed record PriceListResponse(
    int PriceListNo,
    string PriceListName
);
