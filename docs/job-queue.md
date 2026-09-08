# Cola de peticiones a SAP: cómo funciona

## Resumen

El middleware absorbe la saturación del SAP Service Layer (compartido con otros proveedores) en vez de devolver error al llamador. El flujo es:

1. Llega el request → se intenta contra SAP normalmente.
2. Si el Service Layer responde en menos de 10 segundos, sigue igual que siempre (respuesta síncrona, sin tocar la cola).
3. Si no responde a tiempo, o falla porque SAP está saturado (throttle, circuito abierto, timeout), en vez de devolver error se encola:

   ```json
   HTTP 202 Accepted
   {
     "jobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
     "status": "ENCOLADO"
   }
   ```

4. Ese job sigue intentándose en segundo plano con esperas crecientes (15s, 30s, 60s, 120s, 240s) — exactamente para dar tiempo a que el Service Layer se libere de la saturación de otros proveedores.
5. Cuando el intento finalmente logra pasar, el estado cambia a `COMPLETADO` y queda el resultado guardado.
6. Si agota los reintentos sin éxito (5 por defecto), pasa a `ERROR`. Si en cambio SAP responde con un error real de negocio (400/404, no saturación), se marca `ERROR` de una sola vez, sin reintentar — eso no se resuelve esperando.

El cliente se entera de ese cambio de estado consultando `GET /api/v1/jobs/{jobId}`.

Aplica a los 15 endpoints existentes que llaman a SAP (lectura y escritura por igual) — no es un caso especial de un solo endpoint.

## Límite de reintentos

| Intento | Espera antes de intentar |
|---|---|
| 1 | (el intento inicial, antes de encolar) |
| 2 | 15s |
| 3 | 30s |
| 4 | 60s |
| 5 | 120s |
| — | 240s, y si este también falla por saturación → `ERROR`: "SAP saturado: se agotaron los reintentos." |

Máximo **5 reintentos** (`JobQueue:MaxRetryAttempts` en `appsettings.json`), con las esperas de arriba (`JobQueue:RetryBackoffSeconds`). Un error de negocio real de SAP (400/404) no consume reintentos: pasa a `ERROR` de inmediato.

## Operaciones manuales sobre un job encolado

Hoy **no existen** — ni cancelar, ni forzar un reintento inmediato, ni pausar. Una vez que un job queda `ENCOLADO`, sigue su ciclo automático de reintentos hasta terminar en `COMPLETADO` o `ERROR`, sin intervención posible. Lo único disponible es lectura: `GET /api/v1/jobs/{jobId}` (detalle con payload y resultado) y `GET /api/v1/jobs` (listado paginado con filtros por estado, endpoint y fechas, que es lo que muestra la pestaña "Cola SAP" del módulo Integraciones de Nexus).

Si se necesitan a futuro, son 3 endpoints separados, cada uno con una decisión de diseño pendiente:

- **Cancelar** (`DELETE /api/v1/jobs/{jobId}`) — ¿interrumpe un intento en curso o solo evita el próximo reintento?
- **Reintentar ahora** (`POST /api/v1/jobs/{jobId}/retry`) — ¿salta el backoff pendiente, o solo aplica cuando ya terminó en `ERROR`?
- **Pausar** (`POST /api/v1/jobs/{jobId}/pause`) — ¿pausa indefinida hasta reanudar a mano, o "no reintentar por X minutos"?

## Dónde vive cada pieza

| Pieza | Archivo |
|---|---|
| Decide si responde inline o encola (espera los 10s) | `src/Middleware.Infrastructure/Jobs/JobExecutionCoordinator.cs` |
| Reintentos con backoff mientras la falla sea de saturación | `src/Middleware.Infrastructure/Jobs/JobRunner.cs` |
| Clasifica qué excepciones son "saturación" (reintentar) vs "negocio" (error final) | `src/Middleware.Infrastructure/Jobs/JobExceptionClassifier.cs` |
| Persistencia de la cola (SQL Server, `dbo.job_queue`) | `src/Middleware.Infrastructure/Sql/JobQueueRepository.cs` |
| Recupera jobs que quedaron a mitad de camino si el proceso se reinicia | `src/Middleware.Infrastructure/Jobs/JobQueueRecoveryService.cs` |
| Endpoint de consulta de estado | `src/Middleware.Api/Controllers/V1/JobsController.cs` |
| Valores configurables (10s, 5 reintentos, backoff) | `appsettings.json` → sección `JobQueue` |

## Por qué sobrevive un reinicio

Los jobs se guardan en SQL Server (`dbo.job_queue`), no en memoria. Si el middleware se reinicia (deploy, crash) mientras un job está esperando su próximo reintento, `JobQueueRecoveryService` lo detecta al arrancar y retoma los reintentos donde quedaron — el cliente que sigue consultando `GET /api/v1/jobs/{jobId}` no pierde el resultado.

Ver también [docs/concurrency.md](concurrency.md) para las capas de control de concurrencia (throttle, sesión, Polly) que corren *antes* de que un intento se considere fallido o exitoso — la cola es la capa que absorbe lo que esas capas no lograron resolver a tiempo.
