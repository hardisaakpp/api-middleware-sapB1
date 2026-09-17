using Microsoft.AspNetCore.Mvc.Filters;

namespace Middleware.Api.Filters;

/// <summary>
/// Los códigos de artículo con "/" (EM109-070-TM/L) solo pueden viajar en la ruta como %2F, y
/// ASP.NET Core deja ese %2F codificado en el valor de ruta (para no confundirlo con un separador
/// de segmentos). Sin este filtro las acciones recibían el texto literal "EM109-070-TM%2FL" y SAP
/// respondía 404. Decodifica el argumento <c>itemCode</c> una sola vez antes de ejecutar la acción.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DecodeItemCodeRouteAttribute : ActionFilterAttribute
{
    private const string ArgumentName = "itemCode";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionArguments.TryGetValue(ArgumentName, out var value)
            && value is string itemCode
            && itemCode.Contains('%'))
        {
            context.ActionArguments[ArgumentName] = Uri.UnescapeDataString(itemCode);
        }

        base.OnActionExecuting(context);
    }
}
