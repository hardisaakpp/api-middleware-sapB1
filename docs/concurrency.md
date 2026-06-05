# Diseño de concurrencia hacia SAP

## El problema

SAP Business One Service Layer permite un número limitado de sesiones activas por usuario de base de datos. Cuando se supera ese límite, SAP devuelve errores `500` o ODBC `-2028` en cascada. Sin control de acceso, cualquier pico de tráfico puede derribar la integración.

Además, la sesión tiene una vida útil de 30 minutos. Si múltiples requests detectan simultáneamente que la sesión expiró e intentan hacer login al mismo tiempo, se produce una **estampida de autenticación**: decenas de logins paralelos donde solo uno tendrá éxito y el resto fallarán o crearán sesiones huérfanas.

---

## Solución: tres capas de control

```
Request HTTP
    │
    ▼
┌──────────────────────────────────────────────────┐
│  SapThrottle (Singleton)                         │  ← capa 1: límite global de concurrencia
│  SemaphoreSlim(MaxConcurrentSapCalls)            │
└──────────────┬───────────────────────────────────┘
               │  máximo N requests pasan a la vez
               ▼
┌──────────────────────────────────────────────────┐
│  SapSessionManager (Singleton)                   │  ← capa 2: anti-estampida de login
│  SemaphoreSlim(1,1) + double-checked locking     │
└──────────────┬───────────────────────────────────┘
               │  un único token compartido
               ▼
┌──────────────────────────────────────────────────┐
│  Polly Pipeline (por HttpClient)                 │  ← capa 3: resiliencia ante errores transitorios
│  Retry → CircuitBreaker → Timeout (por intento)  │
└──────────────┬───────────────────────────────────┘
               │
               ▼
        SAP Service Layer
```

---

## Capa 1: SapThrottle

**Archivo**: `src/Middleware.Infrastructure/Sap/SapThrottle.cs`

```csharp
internal sealed class SapThrottle
{
    private readonly SemaphoreSlim _semaphore;

    public SapThrottle(IOptions<SapSettings> settings)
    {
        var max = settings.Value.MaxConcurrentSapCalls;
        _semaphore = new SemaphoreSlim(max, max);
    }
}
```

**Por qué es Singleton**: Si fuera Scoped (por request), cada request tendría su propio `SemaphoreSlim` sin efecto global. Con 50 requests concurrentes y throttle=2, habrían 50 instancias que permiten 100 llamadas simultáneas — exactamente lo que queremos evitar.

**Comportamiento**: Los requests que no consiguen slot esperan en la cola del semáforo (`WaitAsync`). No se descartan. El tiempo de espera está limitado por el timeout de Polly (30s por intento).

```
50 requests simultáneos → cola del semáforo (throttle=2)
Batch 1: req#1, req#2  → SAP (200ms)
Batch 2: req#3, req#4  → SAP (200ms)
...
Batch 25: req#49, req#50 → SAP (200ms)
Tiempo total ≈ 25 × 200ms = 5 segundos
Sin throttle: 50 → SAP simultáneamente → probable saturación
```

---

## Capa 2: SapSessionManager — anti-estampida

**Archivo**: `src/Middleware.Infrastructure/Sap/SapSessionManager.cs`

Implementa el patrón **double-checked locking** con un `SemaphoreSlim(1,1)`:

```csharp
public async Task<string> GetSessionTokenAsync(CancellationToken ct)
{
    // Fast path — sin lock, sin overhead (99.9% de los casos)
    if (_sessionToken is not null && DateTimeOffset.UtcNow < _expiresAt)
        return _sessionToken;

    // Slow path — solo cuando la sesión expiró o no existe
    await _semaphore.WaitAsync(ct);
    try
    {
        // Segunda verificación dentro del lock (otro thread ya pudo haber renovado)
        if (_sessionToken is not null && DateTimeOffset.UtcNow < _expiresAt)
            return _sessionToken;

        await LoginAsync(ct);   // un único login
        return _sessionToken!;
    }
    finally
    {
        _semaphore.Release();
    }
}
```

**Escenario con 20 requests cuando la sesión expira**:
1. Los 20 detectan `_sessionToken == null`
2. Todos intentan adquirir el `SemaphoreSlim(1,1)`
3. Solo el primero pasa → hace login → actualiza `_sessionToken`
4. Los 19 restantes entran uno a uno, verifican el double-check → ya hay token → retornan sin login

Resultado: **1 login** en lugar de 20.

**Duración de la sesión**: El token se considera válido por 25 minutos (SAP expira a los 30). El margen de 5 minutos evita usar un token que SAP puede revocar de un momento a otro.

---

## Capa 3: Pipeline de Polly

**Archivo**: `src/Middleware.Infrastructure/Resilience/ResiliencePolicies.cs`

El orden de las estrategias importa: **la primera añadida es la más externa**.

```
Retry (externo)
  └── CircuitBreaker
        └── Timeout (interno, por intento)
              └── HttpClient.SendAsync → SAP
```

### Por qué este orden

| Estrategia | Posición | Razón |
|---|---|---|
| **Retry** | Externa | Cada reintento obtiene su propio presupuesto de timeout (el timeout se resetea en cada intento) |
| **CircuitBreaker** | Media | Cuenta cada intento individual; cuando el circuito abre, el Retry ve `BrokenCircuitException` y falla rápido sin consumir el timeout |
| **Timeout** | Interna | Se aplica a cada llamada HTTP individual, no a toda la operación con reintentos |

**Orden incorrecto** (como estaba antes de la corrección):

```
Timeout (30s total para TODOS los reintentos)
  └── Retry
        └── CircuitBreaker
```

Con ese orden, si el primer intento tarda 10s, solo quedan 20s para los 3 reintentos — el timeout se agota antes de completar los reintentos.

### Parámetros actuales

```csharp
// Retry: 3 reintentos, backoff exponencial con jitter
MaxRetryAttempts = 3
Delay = 1s base, exponential, con jitter
ShouldHandle: HttpRequestException | 5xx | 408 RequestTimeout

// CircuitBreaker
FailureRatio = 0.5    // abre si el 50% de requests fallan
MinimumThroughput = 5  // mínimo 5 requests en la ventana
SamplingDuration = 30s
BreakDuration = 30s    // permanece abierto 30s antes de intentar de nuevo

// Timeout
30 segundos por intento
```

### Relación con HttpClient.Timeout

`HttpClient.Timeout` está configurado en `TimeoutSeconds + 10 = 40s`.  
Como el timeout de Polly (30s) es menor, Polly siempre cancela primero — el `HttpClient.Timeout` es un safety net de último recurso.

---

## Cómo elegir MaxConcurrentSapCalls

1. Ejecutar la prueba de estrés contra SAP (ver [docs/testing.md](testing.md#prueba-de-estrés))
2. Identificar el nivel donde el p95 < 5s y la tasa de errores = 0%
3. Ese nivel es el valor correcto para `Sap:MaxConcurrentSapCalls` en producción
4. Aplicar un margen de seguridad del 20% si SAP es compartido con otros sistemas

**Valores de referencia** (dependen del hardware del servidor SAP):

| Instancia SAP | MaxConcurrentSapCalls recomendado |
|---|---|
| Servidor pequeño / compartido | 2–3 |
| Servidor dedicado medio | 5–8 |
| Servidor dedicado potente | 10–15 |

---

## Manejo de 401 (sesión expirada en medio de un request)

Si SAP devuelve `401 Unauthorized` durante un request activo (la sesión expiró entre el momento de obtenerla y usarla):

```csharp
catch (SapException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
{
    await _sessionManager.InvalidateSessionAsync(ct);   // borra el token cacheado
    var newToken = await _sessionManager.GetSessionTokenAsync(ct);  // re-login (anti-estampida)
    return await _client.SendAsync(method, resource, payload, newToken, ct);
}
```

Este retry manual de 401 está separado del pipeline de Polly porque Polly no reintenta 4xx por defecto (correcto — 401 no es un error transitorio de red, sino de estado de sesión).
