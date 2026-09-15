using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Idempotency;

namespace Middleware.Api.Idempotency;

/// <summary>
/// Resuelve la clave de idempotencia de un POST de creación: <c>u_sap_id</c> del cuerpo, si no la
/// cabecera <c>Idempotency-Key</c>, y si no hay ninguna un GUID solo cuando el endpoint lo permite
/// (los que escriben en una tabla donde el UDF U_sap_id existe con certeza). La clave resuelta se
/// mete en el payload encolado, así todos los reintentos del job usan la misma.
/// </summary>
internal static class IdempotencyKeyResolver
{
    public static (string? Key, ProblemDetails? Problem) Resolve(HttpRequest httpRequest, string? fromBody, bool generateWhenMissing)
    {
        var key = IdempotencyKey.Normalize(fromBody)
                  ?? IdempotencyKey.Normalize(httpRequest.Headers[IdempotencyKey.HeaderName].FirstOrDefault());

        if (key is null)
        {
            return (generateWhenMissing ? IdempotencyKey.NewGenerated() : null, null);
        }

        var error = IdempotencyKey.Validate(key);
        if (error is not null)
        {
            // Se corta aquí y no en SAP: el UDF mide 60 y SAP respondería 400 tras un viaje completo.
            return (null, new ProblemDetails { Title = "Invalid request", Detail = error, Status = StatusCodes.Status400BadRequest });
        }

        return (key, null);
    }
}
