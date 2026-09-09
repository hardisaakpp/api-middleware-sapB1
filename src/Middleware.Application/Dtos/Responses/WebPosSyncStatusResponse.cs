namespace Middleware.Application.Dtos.Responses;

/// <summary>Business reading of pos_sync_status.status, stable for the client UI.</summary>
public static class WebPosIntegrationResults
{
    public const string Integrated = "INTEGRADO";
    public const string NotApplicable = "NO_CORRESPONDE";
    public const string Rejected = "RECHAZADO";
    /// <summary>The notification never reached WEBPOS (connection, configuration, timeout): nothing was rejected, it must be re-sent.</summary>
    public const string Error = "ERROR";
}

/// <summary>Why HITELL rejected a document, reduced to what the client can act on.</summary>
public static class WebPosRejectionTypes
{
    /// <summary>The product is not created in HITELL for that business — create it there, then retry.</summary>
    public const string ProductNotFound = "PRODUCTO_NO_EXISTE";

    /// <summary>Any other HITELL rejection — read <c>items[].message</c>.</summary>
    public const string Other = "OTRO";
}

public sealed record WebPosRejectedItem(
    string? ItemCode,
    string? CodBusiness,
    string Message,
    string Type);

public sealed record WebPosRejectionDetail(
    string Type,
    string Description,
    IReadOnlyList<WebPosRejectedItem> Items,
    string RetryEndpoint);

/// <summary>Almacén del documento y su negocio WEBPOS (null si el almacén no está en WEBPOS). Es donde soporte crea el producto rechazado.</summary>
public sealed record WebPosSyncWarehouse(string Code, string? CodBusiness);

/// <summary>
/// One pos_sync_status row plus its business interpretation: <c>result</c> is what the UI shows
/// (INTEGRADO / NO_CORRESPONDE / RECHAZADO) and <c>rejection</c>, only when RECHAZADO, says
/// whether the fix is "create the product in HITELL" and which endpoint re-integrates it.
/// </summary>
public sealed record WebPosSyncStatusResponse(
    string DocType,
    int DocEntry,
    int? DocNum,
    string Status,
    string Result,
    string? Reason,
    int Attempts,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    WebPosRejectionDetail? Rejection,
    IReadOnlyList<WebPosSyncWarehouse> Warehouses);
