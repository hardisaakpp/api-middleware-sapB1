using System.Linq;
using System.Text.Json;

namespace Middleware.Application.Abstractions;

/// <summary>
/// Lee de una fila de api_log (request + response de un POST que creó un documento en SAP) el
/// DocNum devuelto y los almacenes enviados. Tolerante: los cuerpos se guardan truncados a 2000
/// caracteres y pueden no ser JSON válido; en ese caso devuelve lo que pudo leer.
/// </summary>
public static class ApiLogDocumentTraceParser
{
    public static ApiLogDocumentTrace Parse(string docType, string? request, string? response) =>
        new(ReadDocNum(response), ReadWarehouses(docType, request));

    private static int? ReadDocNum(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(response);
            return TryGetProperty(doc.RootElement, "docNum", out var value) && value.TryGetInt32(out var docNum) ? docNum : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> ReadWarehouses(string docType, string? request)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(request);
            var root = doc.RootElement;
            var codes = new List<string>();

            if (docType == PosSyncDocTypes.GoodsReceipt)
            {
                // Entradas: cada línea trae su bodega receptora.
                if (TryGetProperty(root, "lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
                {
                    foreach (var line in lines.EnumerateArray())
                    {
                        AddIfText(codes, line, "warehouseCode");
                    }
                }
            }
            else
            {
                // Transferencias: origen y destino a nivel de documento.
                AddIfText(codes, root, "fromWarehouse");
                AddIfText(codes, root, "toWarehouse");
            }

            return codes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void AddIfText(List<string> codes, JsonElement element, string property)
    {
        if (TryGetProperty(element, property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                codes.Add(text.Trim());
            }
        }
    }

    /// <summary>Los clientes mandan camelCase o PascalCase; la respuesta del middleware siempre camelCase.</summary>
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        var match = element.ValueKind == JsonValueKind.Object
            ? element.EnumerateObject()
                .Where(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .Select(property => (JsonElement?)property.Value)
                .FirstOrDefault()
            : null;

        value = match ?? default;
        return match is not null;
    }
}
