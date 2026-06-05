# Configuración

El proyecto usa el sistema de configuración estándar de .NET 8.  
Las fuentes se cargan en este orden (cada una sobreescribe la anterior):

```
appsettings.json
  ↓
appsettings.{Environment}.json
  ↓
appsettings.{Environment}.local.json   ← para secretos locales, en .gitignore
  ↓
Variables de entorno (separador __)
  ↓
Argumentos de línea de comandos
```

---

## Sección `Sap`

Controla la conexión con SAP Business One Service Layer.

| Clave | Tipo | Default | Descripción |
|---|---|---|---|
| `Sap:BaseUrl` | string | — | URL base del Service Layer. Debe terminar en `/`. Ej: `http://10.0.0.1:50001/b1s/v1/` |
| `Sap:CompanyDb` | string | — | Base de datos de la empresa SAP (producción). Ej: `SBO_MI_EMPRESA` |
| `Sap:TestCompanyDb` | string | — | Base de datos de empresa SAP para pruebas. Usado por depósitos con `Environment=Test` |
| `Sap:UserName` | string | — | Usuario del Service Layer |
| `Sap:Password` | string | — | Contraseña del usuario SAP |
| `Sap:TimeoutSeconds` | int | `30` | Timeout de cada llamada HTTP a SAP (segundos) |
| `Sap:MaxConcurrentSapCalls` | int | `5` | Máximo de llamadas simultáneas a SAP. Ver [docs/concurrency.md](concurrency.md) para cómo elegir el valor correcto |

### Sobre `MaxConcurrentSapCalls`

Este es el parámetro más crítico para la estabilidad. Un valor demasiado alto puede saturar SAP; demasiado bajo reduce el throughput innecesariamente.

Regla de decisión:
- Valor seguro de inicio: **5**
- Ejecutar la [prueba de estrés](testing.md#prueba-de-estrés) para encontrar el límite real de tu instancia SAP
- El último nivel sin errores y con p95 < 5s es el valor correcto para producción

---

## Sección `Sql`

| Clave | Tipo | Default | Descripción |
|---|---|---|---|
| `Sql:ConnectionString` | string | — | Cadena de conexión completa a SQL Server |

Ejemplo:
```
Server=10.0.0.2;Database=STORECONTROL;User Id=lectura;Password=xxx;Encrypt=false;TrustServerCertificate=true;
```

---

## Sección `Serilog`

Controla los niveles de log por namespace. En `appsettings.Development.json`:

```json
{
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
}
```

En producción se recomienda subir `Default` a `Warning` o `Error` para reducir volumen de logs.

Los archivos de log se escriben en `src/Middleware.Api/logs/middleware-YYYYMMDD.json`.  
Development: retiene 7 días. Production: retiene 30 días.

---

## Sección `OpenTelemetry`

| Clave | Tipo | Default | Descripción |
|---|---|---|---|
| `OpenTelemetry:Endpoint` | string | `""` | Endpoint OTLP. Si está vacío, las trazas y métricas no se exportan |

También se puede configurar con la variable de entorno estándar:
```
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
```

---

## Variables de entorno (Docker / Kubernetes)

Las secciones anidadas usan `__` (doble guión bajo) como separador:

```bash
Sap__BaseUrl=http://sap-server:50001/b1s/v1/
Sap__CompanyDb=SBO_PROD
Sap__UserName=usuario
Sap__Password=contraseña
Sap__MaxConcurrentSapCalls=10
Sql__ConnectionString=Server=sql;Database=DB;...
OpenTelemetry__Endpoint=http://otel:4317
```

---

## Secretos en producción

Las contraseñas y cadenas de conexión **nunca** deben estar en el repositorio.  
Opciones recomendadas según el entorno de despliegue:

| Entorno | Mecanismo recomendado |
|---|---|
| Local (desarrollo) | `appsettings.Development.local.json` (en `.gitignore`) |
| Docker Compose | Variables de entorno en `docker-compose.override.yml` (en `.gitignore`) |
| Kubernetes | `Secret` + montaje como variables de entorno |
| Azure | Azure Key Vault + Managed Identity |
| AWS | AWS Secrets Manager + IAM Role |

---

## Archivo de referencia completo

```json
{
  "Sap": {
    "BaseUrl": "http://sap-server:50001/b1s/v1/",
    "CompanyDb": "SBO_EMPRESA_PROD",
    "TestCompanyDb": "SBO_EMPRESA_TEST",
    "UserName": "usuario-api",
    "Password": "contraseña-segura",
    "TimeoutSeconds": 30,
    "MaxConcurrentSapCalls": 5
  },
  "Sql": {
    "ConnectionString": "Server=sql-server;Database=STORECONTROL;User Id=user;Password=pass;Encrypt=false;"
  },
  "OpenTelemetry": {
    "Endpoint": "http://otel-collector:4317"
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```
