# api-middleware-sap

Middleware .NET 8 que actúa como **único punto de acceso** a SAP Business One Service Layer y SQL Server.  
Regula la concurrencia de llamadas salientes, gestiona la sesión de SAP y expone una API REST versionada para los consumidores internos.

---

## ¿Por qué existe este servicio?

SAP Business One Service Layer impone un límite estricto de sesiones concurrentes por usuario. Sin un intermediario, varios sistemas llamando directamente a SAP generan:

- Saturación de sesiones → errores 500/ODBC en cascada
- Login duplicado bajo concurrencia → estampida de autenticación
- Sin retry ni circuit breaker → fallos no recuperables ante errores transitorios

Este middleware resuelve los tres problemas siendo el **único punto de salida** hacia SAP.

---

## Quickstart

### Requisitos

| Herramienta | Versión mínima |
|---|---|
| .NET SDK | 8.0.421 (ver `global.json`) |
| SAP Business One Service Layer | acceso de red + credenciales |
| SQL Server | acceso + stored procedure `sp_GetAvailableStock` |

### 1. Configurar credenciales

Crea `src/Middleware.Api/appsettings.Development.local.json` (ignorado por `.gitignore`):

```json
{
  "Sap": {
    "BaseUrl": "http://tu-servidor-sap:50001/b1s/v1/",
    "CompanyDb": "SBO_TU_EMPRESA",
    "UserName": "tu-usuario",
    "Password": "tu-contraseña"
  },
  "Sql": {
    "ConnectionString": "Server=tu-servidor;Database=TU_DB;User Id=user;Password=pass;Encrypt=false;"
  }
}
```

> **Nunca** hagas commit de credenciales, URLs de producción ni secretos.

### 2. Ejecutar

```bash
dotnet run --project src/Middleware.Api
```

La API arranca en `http://localhost:5250`.  
Swagger UI (solo Development): `http://localhost:5250/swagger`  
Health check: `http://localhost:5250/health`

### 3. Verificar

```bash
curl http://localhost:5250/health
# {"status":"Healthy"}

curl http://localhost:5250/api/v1/purchase-orders/1
# {"docEntry":1,"docNum":100,...}
```

### 4. Tests

```bash
# Tests de integración (sin SAP real)
dotnet test

# Prueba de estrés (requiere SAP real, ejecutar a mano)
dotnet test --filter "Category=stress" -v normal
```

---

## Estructura del proyecto

```
src/
├── Middleware.Api            — Arranque, controladores, middlewares HTTP, health checks
├── Middleware.Application    — Casos de uso, DTOs, validaciones, interfaces públicas
├── Middleware.Domain         — Entidades, enumeraciones, errores — sin dependencias externas
└── Middleware.Infrastructure — SAP Service Layer, SQL Server, Polly, OpenTelemetry, Serilog

tests/
├── Middleware.IntegrationTests   — Tests de integración (WebApplicationFactory)
└── Middleware.UnitTests          — Tests unitarios (Application + Domain)
```

---

## Documentación técnica

| Documento | Contenido |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Capas, ciclo de vida de un request, servicios background, manejo de errores |
| [docs/api.md](docs/api.md) | Referencia completa de endpoints |
| [docs/configuration.md](docs/configuration.md) | Todas las claves de configuración |
| [docs/concurrency.md](docs/concurrency.md) | Diseño del throttle, sesión SAP, Polly |
| [docs/testing.md](docs/testing.md) | Estrategia de tests, cómo añadir nuevos |

---

## Docker

```bash
docker build -t api-middleware-sap .
docker run -p 8080:8080 \
  -e Sap__BaseUrl="http://sap:50001/b1s/v1/" \
  -e Sap__CompanyDb="SBO_PROD" \
  -e Sap__UserName="user" \
  -e Sap__Password="pass" \
  api-middleware-sap
```

Variables de entorno usan `__` como separador de sección (convención .NET).
