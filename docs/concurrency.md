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

---

## Ciclo de vida de conexiones HTTP (`PooledConnectionLifetime`)

**Archivo**: `src/Middleware.Infrastructure/DependencyInjection.cs` (`BuildSapHandler`)

### El problema (detectado 2026-07-14 en logs de producción)

`SocketsHttpHandler` reutiliza conexiones TCP del pool indefinidamente por defecto
(`PooledConnectionLifetime = Timeout.InfiniteTimeSpan`). El servidor de SAP Service Layer
cierra conexiones keep-alive inactivas por su lado antes de que .NET las dé de baja, así
que una conexión que ya está muerta del lado servidor puede seguir en el pool del cliente.

Cuando el middleware reutiliza esa conexión ya cerrada, el request falla a mitad de camino
con `HttpIOException: The response ended prematurely`. Polly lo reintenta (correcto, es un
`HttpRequestException`), pero eso agrega un ciclo completo de backoff (~1s base + jitter)
más la reconexión — **overhead 100% evitable** que se sentía como lentitud en el envío de
ODCs y STs, agravado porque se crea una `InventoryTransferRequest` por cada bodega destino
del pedido (el overhead se acumula linealmente con la cantidad de bodegas).

Ejemplo real del log: intento 0 falla en 57.9ms (conexión muerta), intento 1 tarda 324.5ms
y sí llega a SAP — total 1699ms para ese request, de los cuales ~1.3-1.7s son puro overhead
de reconexión, no trabajo real contra SAP Service Layer.

### El fix

```csharp
private static SocketsHttpHandler BuildSapHandler() => new()
{
    SslOptions = new SslClientAuthenticationOptions { /* ... */ },
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(45),
    PooledConnectionLifetime = TimeSpan.FromMinutes(3)
};
```

- `PooledConnectionIdleTimeout` (45s): una conexión que lleva más de 45s sin usarse se
  descarta del pool proactivamente, en vez de esperar a que un request real descubra que
  ya está muerta. Evita justo el escenario del log.
- `PooledConnectionLifetime` (3 min): rota conexiones periódicamente incluso bajo uso
  constante, como defensa adicional (cambios de DNS, balanceo, etc.).

Ambos valores son deliberadamente más cortos que el timeout de keep-alive típico de la
mayoría de servidores de aplicación — el objetivo es que el cliente cierre la conexión
*antes* de que el servidor lo haga, nunca después.

### Qué no arregla esto

Este fix reduce el overhead de reconexión, pero no toca:
- Errores de negocio de SAP (ej. `-1116` de `SBO_SP_TRANSACTIONNOTIFICATION` sin soporte
  para `InventoryTransferRequests`) — eso es una customización de la base de compañía SAP,
  fuera del alcance del middleware.
- La latencia base de crear N documentos secuenciales (una ODC con transferencias a M
  bodegas sigue siendo M llamadas). Para eso, ver el pendiente "job durable ODC+ITRs" en el
  backlog del proyecto.
