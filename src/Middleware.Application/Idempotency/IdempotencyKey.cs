using System.Text.RegularExpressions;

namespace Middleware.Application.Idempotency;

/// <summary>
/// Reglas de la clave de idempotencia que los módulos envían al crear documentos en SAP y que el
/// middleware guarda en el UDF <c>U_sap_id</c> de la cabecera del documento. Convención completa
/// en docs/idempotencia-documentos.md.
/// </summary>
public static partial class IdempotencyKey
{
    /// <summary>Tamaño del UDF U_sap_id en SAP (alfanumérico, 60; confirmado 11/09/2026 en OWTR).</summary>
    public const int MaxLength = 60;

    /// <summary>Cabecera HTTP alternativa al campo <c>u_sap_id</c> del cuerpo (la envía api-recepcion-mercaderia).</summary>
    public const string HeaderName = "Idempotency-Key";

    /// <summary>Letras, dígitos, guion, guion bajo, punto y dos puntos; empieza por letra o dígito.</summary>
    public const string AllowedPattern = "^[A-Za-z0-9][A-Za-z0-9._:-]*$";

    public static string FormatMessage =>
        $"La clave de idempotencia (u_sap_id / {HeaderName}) admite solo letras, dígitos y los signos . _ : - , empezando por letra o dígito.";

    /// <summary>Quita espacios y trata vacío como ausente.</summary>
    public static string? Normalize(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    /// <summary>Mensaje de error si la clave no cumple tamaño o formato; null si es válida.</summary>
    public static string? Validate(string key)
    {
        if (key.Length > MaxLength)
        {
            return $"La clave de idempotencia (u_sap_id / {HeaderName}) tiene {key.Length} caracteres; el máximo es {MaxLength} (tamaño del UDF U_sap_id en SAP).";
        }

        return AllowedRegex().IsMatch(key) ? null : FormatMessage;
    }

    /// <summary>Clave generada por el middleware cuando el módulo no envía ninguna: GUID "D" en mayúsculas, el mismo formato que usa api-transferencias-directas.</summary>
    public static string NewGenerated() => Guid.NewGuid().ToString("D").ToUpperInvariant();

    [GeneratedRegex(AllowedPattern)]
    private static partial Regex AllowedRegex();
}
