namespace Middleware.Api.Controllers.V1.Queries;

/// <summary>
/// Query string de <c>GET /api-logs</c>. Se enlaza como objeto ([FromQuery]) para no tener un método
/// con nueve parámetros; los nombres de las propiedades son los de la query string.
/// </summary>
public sealed class ApiLogsListQuery
{
    /// <summary>Búsqueda por contenido en el endpoint, p. ej. "stock-transfers".</summary>
    public string? Endpoint { get; init; }

    /// <summary>Búsqueda por contenido en el usuario.</summary>
    public string? Usuario { get; init; }

    /// <summary>Código HTTP exacto.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Solo respuestas con status >= 400.</summary>
    public bool OnlyErrors { get; init; }

    /// <summary>Desde (fecha en hora local de Ecuador, inclusiva).</summary>
    public DateTime? From { get; init; }

    /// <summary>Hasta (fecha en hora local de Ecuador, inclusiva).</summary>
    public DateTime? To { get; init; }

    public int Page { get; init; } = 1;

    /// <summary>Máximo 200.</summary>
    public int PageSize { get; init; } = 20;
}
