namespace Middleware.Application.Dtos.Responses;

public sealed record WarehousesListResponse(
    IReadOnlyList<WarehouseResponse> Warehouses
);

public sealed record WarehouseResponse(
    string WhsCode,
    string WhsName
);

public sealed record ValidateWarehousesResponse(
    IReadOnlyList<WarehouseValidationResult> Warehouses
);

public sealed record WarehouseValidationResult(
    string WhsCode,
    bool Exists,
    string? WhsName,
    string? ErrorType,
    string? ErrorMessage = null
);
