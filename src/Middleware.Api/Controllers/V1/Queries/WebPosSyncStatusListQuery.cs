using Middleware.Application.UseCases.WebPos;

namespace Middleware.Api.Controllers.V1.Queries;

/// <summary>
/// Query string de <c>GET /webpos/sync-status</c>. Se enlaza como objeto ([FromQuery]) para no tener
/// un método con diez parámetros; los nombres de las propiedades son los de la query string.
/// </summary>
public sealed class WebPosSyncStatusListQuery
{
    /// <summary>SYNCED | REJECTED | SKIPPED_NOT_POS.</summary>
    public string? Status { get; init; }

    /// <summary>ST | GR.</summary>
    public string? DocType { get; init; }

    /// <summary>PRODUCTO_NO_EXISTE | OTRO (implica status=REJECTED).</summary>
    public string? RejectionType { get; init; }

    public int? DocEntry { get; init; }

    /// <summary>Número de documento SAP, lo que ve el usuario.</summary>
    public int? DocNum { get; init; }

    /// <summary>Desde (sobre la última actualización, inclusiva).</summary>
    public DateTime? From { get; init; }

    /// <summary>Hasta (sobre la última actualización, inclusiva).</summary>
    public DateTime? To { get; init; }

    public int Page { get; init; } = 1;

    /// <summary>Máximo 200.</summary>
    public int PageSize { get; init; } = 20;

    public WebPosSyncStatusQuery ToQuery() =>
        new(Status, DocType, RejectionType, DocEntry, DocNum, From, To, Page, PageSize);
}
