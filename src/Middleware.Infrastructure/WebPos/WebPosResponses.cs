using System.Text.Json;

namespace Middleware.Infrastructure.WebPos;

/// <summary>Helpers to read WEBPOS answer bodies (they carry business outcomes in HTTP 200 and 400 alike).</summary>
public static class WebPosResponses
{
    /// <summary>True for WEBPOS' "El token de acceso es inválido o esta expirado." answer (HTTP 400, procesoCorrecto=false).</summary>
    public static bool IsTokenRejected(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("procesoCorrecto", out var pc) || pc.ValueKind != JsonValueKind.False ||
                !root.TryGetProperty("mensaje", out var m) || m.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var mensaje = m.GetString() ?? string.Empty;
            return mensaje.Contains("token", StringComparison.OrdinalIgnoreCase) &&
                   (mensaje.Contains("inv", StringComparison.OrdinalIgnoreCase) || mensaje.Contains("expir", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
