# Testing

## Estrategia de tests

```
                        ▲ más lento / más fiel
                        │
          ┌─────────────────────────┐
          │    Stress Tests         │  prueba de estrés real contra SAP — manual
          ├─────────────────────────┤
          │  Integration Tests      │  API completa, sin SAP real — CI/CD
          ├─────────────────────────┤
          │    Unit Tests           │  lógica de Application y Domain — CI/CD
          └─────────────────────────┘
                        │
                        ▼ más rápido / más aislado
```

| Proyecto | Scope | SAP real | CI |
|---|---|---|---|
| `Middleware.UnitTests` | Application, Domain | No | ✓ |
| `Middleware.IntegrationTests` | API completa (controladores, middlewares, gateway) | No (fake) | ✓ |
| `SapCapacityTest` (dentro de IntegrationTests) | Capacidad real de SAP | **Sí** | ✗ (manual) |

---

## Ejecutar los tests

```bash
# Todos los tests (unit + integration) — sin SAP real
dotnet test

# Solo tests de integración
dotnet test tests/Middleware.IntegrationTests

# Solo unit tests
dotnet test tests/Middleware.UnitTests

# Ver output detallado
dotnet test -v normal

# Filtrar por nombre
dotnet test --filter "FullyQualifiedName~PurchaseOrder"
```

---

## Tests de integración

Usan `WebApplicationFactory<Program>` de `Microsoft.AspNetCore.Mvc.Testing`.  
El app arranca en memoria — no hace falta levantar el proceso manualmente.

### Patrón base: reemplazar ISapGateway con un mock

Para tests que solo verifican comportamiento HTTP (status codes, headers, body):

```csharp
public sealed class MiControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MiControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(ISapGateway));
                if (descriptor is not null) services.Remove(descriptor);

                var mock = new Mock<ISapGateway>();
                mock.Setup(g => g.CreateStockTransferAsync(
                        It.IsAny<CreateStockTransferRequest>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DocumentCreatedResult(42, 1001));

                services.AddScoped<ISapGateway>(_ => mock.Object);
            });
        });
    }

    [Fact]
    public async Task Post_Returns201()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/stock-transfers", new { ... });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
```

### Patrón avanzado: reemplazar ISapServiceLayerClient (para tests de throttle)

Para tests que necesitan verificar comportamiento de concurrencia, Polly o session management — donde el `SapGateway` real debe ejecutarse:

```csharp
// SapConcurrencyFactory reemplaza ISapServiceLayerClient e ISapSessionManager
// manteniendo SapGateway y SapThrottle reales
await using var factory = new SapConcurrencyFactory(sapCallDelay: TimeSpan.FromMilliseconds(150));
var client = factory.CreateClient();
```

Ver `tests/Middleware.IntegrationTests/Infrastructure/SapConcurrencyFactory.cs` y  
`tests/Middleware.IntegrationTests/Concurrency/SapThrottleTests.cs` para la implementación completa.

### Qué cubre cada capa del mock

| Reemplazar | Tests que habilita | Tests que no cubre |
|---|---|---|
| `ISapGateway` (mock) | Status codes, validación, headers, ProblemDetails | Throttle, Polly, session management |
| `ISapServiceLayerClient` (fake) | Throttle, Polly retry, session anti-stampede | Respuestas reales de SAP |
| Nada (SAP real) | Todo lo anterior + fidelidad real | Velocidad, disponibilidad de SAP |

---

## Añadir un test de integración para un nuevo endpoint

1. Crear `tests/Middleware.IntegrationTests/Controllers/MiNuevoControllerTests.cs`
2. Usar `IClassFixture<WebApplicationFactory<Program>>` si todos los tests del archivo comparten el mismo mock
3. Usar `WithWebHostBuilder` dentro del constructor para configurar el mock
4. Verificar: status code, body, header `X-Correlation-Id`, casos de error (400, 404, 502)

Ejemplo completo de verificación de error 404 propagado desde SAP:

```csharp
[Fact]
public async Task Get_WhenSapReturnsNull_Returns404()
{
    var factory = _factory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISapGateway));
            if (descriptor is not null) services.Remove(descriptor);

            var mock = new Mock<ISapGateway>();
            mock.Setup(g => g.GetPurchaseOrderAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PurchaseOrderResponse?)null);

            services.AddScoped<ISapGateway>(_ => mock.Object);
        });
    });

    var client = factory.CreateClient();
    var response = await client.GetAsync("/api/v1/purchase-orders/99999");

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
}
```

---

## Prueba de estrés

**Requiere SAP real. Ejecutar solo manualmente, nunca en CI.**

```bash
dotnet test --filter "Category=stress" -v normal
```

Qué hace:
1. Arranca el middleware apuntando al SAP real (usa `appsettings.Development.json`)
2. Prueba niveles de concurrencia: 1 → 2 → 5 → 10 → 20 llamadas simultáneas a SAP
3. En cada nivel lanza 30 requests y mide min/p50/p95/p99/max y tasa de error
4. Se detiene automáticamente si los errores superan el 20%

Output esperado:

```
SAP Capacity Stress Test
Requests per phase : 30
Concurrency levels : 1 → 2 → 5 → 10 → 20
────────────────────────────────────────────────────────────────────────
 Level     OK    Err  ErrRate     min     p50     p95     p99     max
────────────────────────────────────────────────────────────────────────
     1     30      0       0%   312ms   340ms   410ms   430ms   445ms
     2     30      0       0%   290ms   350ms   520ms   540ms   560ms
     5     30      0       0%   310ms   480ms   890ms   940ms   970ms
    10     30      0       0%   380ms   820ms  2100ms  2800ms  3100ms
    20     25      5      17%   400ms  1200ms  8500ms 12000ms 15000ms
────────────────────────────────────────────────────────────────────────
Aborting: error rate 17% at concurrency 20 exceeds threshold.
SAP limit is around the previous phase.
```

El resultado indica que `MaxConcurrentSapCalls = 10` es el límite seguro para ese servidor SAP.  
Ver [docs/configuration.md](configuration.md#sobre-maxconcurrentsapcalls) para aplicar el resultado.

---

## Tests de concurrencia del throttle

Verifican que `SapThrottle` funciona correctamente sin necesidad de SAP real.

```bash
dotnet test --filter "FullyQualifiedName~SapThrottle"
```

Estos tests corren en CI. Usan un `FakeSapServiceLayerClient` con delay de 150ms para que la superposición de requests sea observable. Si el throttle estuviera roto, `PeakConcurrentCalls` superaría el límite configurado y el test fallaría.

---

## Checklist al añadir una nueva operación SAP

- [ ] Método en `ISapGateway` (Application)
- [ ] Implementación en `SapGateway` con catch de `SapException NotFound` si aplica
- [ ] Use case en Application
- [ ] Controller en Api
- [ ] Request DTO con FluentValidation
- [ ] Response DTO
- [ ] Test de integración con mock de `ISapGateway`
  - [ ] Caso exitoso (201/200)
  - [ ] Caso de error de validación (400)
  - [ ] Caso de recurso no encontrado en SAP (404) si aplica
- [ ] Registrar el use case en `Application/DependencyInjection.cs`
