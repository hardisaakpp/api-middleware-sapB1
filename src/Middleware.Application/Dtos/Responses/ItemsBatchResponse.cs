namespace Middleware.Application.Dtos.Responses;

public sealed record ItemsBatchResponse(
    IReadOnlyList<ItemResponse> Items,
    IReadOnlyList<string> NotFound
);
