# Arquitectura

## Capas (Clean Architecture)

El proyecto sigue Clean Architecture con cuatro capas. La regla de dependencias es estricta: **las capas internas no conocen las externas**.

```
┌─────────────────────────────────────────┐
│              Middleware.Api             │  ← arranque, HTTP, controllers
│         Middleware.Infrastructure       │  ← SAP, SQL, Polly, logs
├─────────────────────────────────────────┤
│           Middleware.Application        │  ← casos de uso, DTOs, validaciones
├─────────────────────────────────────────┤
│             Middleware.Domain           │  ← entidades, enums, errores
└─────────────────────────────────────────┘
```

### Por qué esta separación

| Capa | Regla | Razón |
|---|---|---|
| **Domain** | Sin dependencias externas | Las reglas de negocio no deben cambiar si cambia SAP o el framework |
| **Application** | Solo depende de Domain | Los casos de uso solo conocen interfaces; la infraestructura es inyectable |
| **Infrastructure** | Implementa interfaces de Application | Permite reemplazar SAP o SQL en tests sin tocar la lógica |
| **Api** | Orquesta Application + Infrastructure | El arranque y el HTTP son detalles de infraestructura |

---

## Ciclo de vida de un request

```
HTTP Request
    │
    ▼
CorrelationIdMiddleware          — asigna/propaga X-Correlation-Id
    │
    ▼
SerilogRequestLogging            — loguea método, path, status, tiempo
    │
    ▼
GlobalExceptionMiddleware        — captura excepciones, mapea a ProblemDetails
    │
    ▼
Controller (Api)                 — valida binding, delega al use case
    │
    ▼
UseCase (Application)            — orquesta la operación, loguea intención
    │
    ▼
ISapGateway (Infrastructure)     — llama a SAP con throttle + session
    │
    ▼
SapThrottle → SemaphoreSlim      — limita llamadas concurrentes a SAP
    │
    ▼
ISapSessionManager               — devuelve token (cacheado o nuevo login)
    │
    ▼
ISapServiceLayerClient           — HttpClient con pipeline Polly
    │
    ▼
SAP Business One Service Layer
```

---

## Lifetimes de los servicios DI

| Servicio | Lifetime | Motivo |
|---|---|---|
| `SapSessionManager` | **Singleton** | La sesión (token) debe compartirse entre todos los requests |
| `SapThrottle` | **Singleton** | El `SemaphoreSlim` que limita concurrencia debe ser global — si fuera Scoped cada request tendría su propio semáforo sin efecto |
| `SapGateway` | **Scoped** | Depende de `ISapServiceLayerClient` (typed HttpClient, Transient); no puede ser Singleton para evitar capturar dependencias de vida corta |
| `ISapServiceLayerClient` | **Transient** | Typed HttpClient — el factory gestiona el pool de handlers internamente |
| `SqlRepository` | **Scoped** | Una conexión SQL por request HTTP |
| Use Cases | **Scoped** | Sin estado compartido entre requests |

> **Regla práctica**: si un Singleton depende de un Scoped, el contenedor lanzará una excepción en startup. Esta pirámide de lifetimes está diseñada para evitar ese problema.

---

## Manejo de errores

### Jerarquía de excepciones SAP

```
Exception
└── SapException           — SAP devolvió un HTTP no-2xx (tiene StatusCode + SapError raw)
└── SapResponseException   — SAP devolvió 2xx pero el JSON no tiene los campos esperados
```

### Mapeo en GlobalExceptionMiddleware

| Excepción | HTTP Status | Log Level |
|---|---|---|
| `SapException { NotFound }` | 404 | Warning |
| `SapException` (otros) | 502 Bad Gateway | Warning |
| `SapResponseException` | 502 Bad Gateway | Error |
| `ArgumentException` | 400 | — |
| `OperationCanceledException` | 503 | — |
| Cualquier otra | 500 | Error + stack trace |

Todas las respuestas de error usan el formato estándar `ProblemDetails` (RFC 7807) con el campo adicional `correlationId` para trazabilidad.

### Correlación de requests

`CorrelationIdMiddleware` genera o propaga el header `X-Correlation-Id`:
- Si el request trae el header, lo reutiliza (útil cuando el consumidor ya tiene una traza)
- Si no trae header, genera un `Guid` nuevo
- El ID se añade a todos los logs del request y se devuelve en el response header

---

## Versionado de la API

Se usa `Asp.Versioning` con versión en la URL (`/api/v1/...`).  
La versión por defecto es `1.0` — los consumidores que no especifiquen versión reciben v1.

Para añadir una v2 en el futuro:
1. Crear `Controllers/V2/` con los nuevos controllers
2. Decorar con `[ApiVersion("2.0")]`
3. El contrato v1 sigue funcionando sin cambios

---

## Observabilidad

| Componente | Implementación |
|---|---|
| Logs estructurados | Serilog + CompactJsonFormatter → archivos `logs/middleware-YYYYMMDD.json` |
| Trazas distribuidas | OpenTelemetry → OTLP exporter (configurable) |
| Métricas | OpenTelemetry AspNetCore + HttpClient instrumentation |
| Health check | `/health` (liveness) · `/health/ready` (readiness, verifica sesión SAP) |

Los logs en Development también se escriben en consola.  
El campo `CorrelationId` aparece en todos los logs de un mismo request.

### Niveles de log por namespace (Development)

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft": "Warning",
      "System": "Warning",
      "Middleware": "Debug"
    }
  }
}
```

El namespace `Middleware` muestra Debug en Development, lo que incluye los logs de `SAP → GET PurchaseOrders(1)`.
