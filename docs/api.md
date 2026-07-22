# Referencia de API

Base URL: `http://localhost:5250/api/v1`  
Todos los cuerpos de request y response son `application/json`.  
Todos los responses de error siguen el formato `ProblemDetails` (RFC 7807).

---

## Headers comunes

### Request

| Header | Descripción |
|---|---|
| `X-Correlation-Id` | Opcional. ID de correlación para trazabilidad. Si no se envía, el servidor genera uno. |

### Response

| Header | Descripción |
|---|---|
| `X-Correlation-Id` | Siempre presente. El mismo ID que vino en el request, o el generado por el servidor. |

---

## Cola de peticiones (procesamiento asíncrono)

Todos los endpoints que llaman a SAP funcionan igual hoy si SAP responde rápido: se intenta el request de forma síncrona y se devuelve el resultado normal (200/201/404/etc.).

Si SAP no responde en **10 segundos** (configurable en `JobQueue.QueueAfterSeconds`), o si la llamada falla por saturación (SAP ocupado, circuito abierto, timeout), el request **no se pierde ni falla**: se encola de forma durable (sobrevive un reinicio del middleware) y se responde inmediatamente:

```json
HTTP 202 Accepted
{
  "jobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "ENCOLADO"
}
```

El job encolado se reintenta en segundo plano con backoff creciente (`JobQueue.RetryBackoffSeconds`) mientras la falla sea de saturación. Si falla por un error real de negocio en SAP (400/404), se marca como error sin reintentar.

### GET /jobs/{jobId}

Consulta el estado de un job encolado.

**Respuesta — 200 OK**

```json
{
  "jobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "endpoint": "PATCH /api/v1/inventory-transfer-requests/396536/status",
  "status": "COMPLETADO",
  "httpStatusCode": 200,
  "result": { "success": true, "docEntry": 396536, "u_STATUS": "1" },
  "error": null,
  "attempts": 2,
  "createdAt": "2026-07-21T10:15:00Z",
  "updatedAt": "2026-07-21T10:15:47Z"
}
```

`status` es uno de: `ENCOLADO` (esperando o reintentando), `PROCESANDO` (intentando ahora mismo), `COMPLETADO` o `ERROR`. Mientras está `ENCOLADO`/`PROCESANDO`, `result` es `null`. Si termina en `ERROR`, `error` trae el motivo y `result` es `null`.

**Códigos de error**

| Status | Cuándo |
|---|---|
| `404 Not Found` | El `jobId` no existe |

---

## Órdenes de Compra (Purchase Orders)

### GET /purchase-orders/{docEntry}

Obtiene una orden de compra de SAP por su `DocEntry`.

**Parámetros de ruta**

| Parámetro | Tipo | Descripción |
|---|---|---|
| `docEntry` | `int` | Identificador interno de SAP (DocEntry, no DocNum) |

**Respuesta exitosa — 200 OK**

```json
{
  "docEntry": 23,
  "docNum": 1045,
  "cardCode": "P001",
  "docDate": "2024-06-01",
  "documentStatus": "bost_Open",
  "documentLines": [
    {
      "lineNum": 0,
      "itemCode": "ITEM-001",
      "quantity": 10.0,
      "openQuantity": 6.0,
      "unitPrice": 250.00,
      "warehouseCode": "WH01"
    }
  ]
}
```

**Códigos de error**

| Status | Cuándo |
|---|---|
| `404 Not Found` | El `DocEntry` no existe en SAP |
| `502 Bad Gateway` | SAP devolvió un error inesperado |

---

### PATCH /purchase-orders/{docEntry}

Actualiza líneas de una orden de compra existente en SAP.

**Cuerpo del request**

```json
{
  "lines": [
    {
      "lineNum": 0,
      "quantity": 8.0,
      "warehouseCode": "WH02"
    }
  ]
}
```

| Campo | Tipo | Requerido | Descripción |
|---|---|---|---|
| `lines` | array | ✓ | Al menos un elemento |
| `lines[].lineNum` | int | ✓ | Número de línea (≥ 0) |
| `lines[].quantity` | decimal | ✓ | Nueva cantidad (> 0) |
| `lines[].warehouseCode` | string | — | Nuevo código de almacén |

**Respuesta exitosa — 200 OK**

```json
{
  "success": true,
  "docEntry": 23
}
```

**Códigos de error**

| Status | Cuándo |
|---|---|
| `400 Bad Request` | Validación fallida (lineNum < 0, quantity ≤ 0, lines vacío) |
| `404 Not Found` | El `DocEntry` no existe en SAP |
| `502 Bad Gateway` | SAP devolvió un error inesperado |

---

## Solicitudes de Transferencia de Inventario (Inventory Transfer Requests)

### PATCH /inventory-transfer-requests/{docEntry}/status

Actualiza el campo de usuario `U_STATUS` de una solicitud de transferencia de inventario existente en SAP (`InventoryTransferRequests`).

**Parámetros de ruta**

| Parámetro | Tipo | Descripción |
|---|---|---|
| `docEntry` | `int` | Identificador interno de SAP (DocEntry, no DocNum) |

**Cuerpo del request**

```json
{
  "U_STATUS": "1"
}
```

| Campo | Tipo | Requerido | Descripción |
|---|---|---|---|
| `U_STATUS` | string | ✓ | Valor aceptado: `"0"`, `"1"` o `"2"` |

**Respuesta exitosa — 200 OK**

```json
{
  "success": true,
  "docEntry": 396536,
  "u_STATUS": "1"
}
```

**Códigos de error**

| Status | Cuándo |
|---|---|
| `400 Bad Request` | `U_STATUS` no es `"0"`, `"1"` o `"2"` |
| `404 Not Found` | El `DocEntry` no existe en SAP |
| `502 Bad Gateway` | SAP devolvió un error inesperado |

---

## Transferencias de Stock (Stock Transfers)

### POST /stock-transfers

Crea una transferencia de stock entre almacenes en SAP (`StockTransfers`).

**Cuerpo del request**

```json
{
  "fromWarehouse": "WH01",
  "toWarehouse": "WH02",
  "docDate": "2024-06-15",
  "comments": "Reposición mensual",
  "lines": [
    {
      "itemCode": "ITEM-001",
      "quantity": 5.0,
      "projectCode": "PROJ-A"
    }
  ]
}
```

| Campo | Tipo | Requerido | Validación |
|---|---|---|---|
| `fromWarehouse` | string | ✓ | NotEmpty, max 8 chars, distinto de `toWarehouse` |
| `toWarehouse` | string | ✓ | NotEmpty, max 8 chars |
| `docDate` | `DateOnly` | ✓ | Formato `yyyy-MM-dd` |
| `comments` | string | — | Opcional |
| `lines` | array | ✓ | Al menos un elemento |
| `lines[].itemCode` | string | ✓ | NotEmpty, max 20 chars |
| `lines[].quantity` | decimal | ✓ | > 0 |
| `lines[].projectCode` | string | — | Opcional |

**Respuesta exitosa — 201 Created**

```json
{
  "docEntry": 42,
  "docNum": 1001
}
```

---

### POST /stock-transfers/with-bins

Crea una transferencia de stock con asignación de bins (ubicaciones de almacén).

**Cuerpo del request**

```json
{
  "fromWarehouse": "WH01",
  "toWarehouse": "WH02",
  "docDate": "2024-06-15",
  "comments": null,
  "lines": [
    {
      "itemCode": "ITEM-001",
      "quantity": 5.0,
      "fromBinCode": 10,
      "toBinCode": 25
    }
  ]
}
```

Los campos `fromBinCode` y `toBinCode` son los `BinAbsEntry` de SAP (entero, no el código visual).

**Respuesta exitosa — 201 Created**

```json
{
  "docEntry": 43,
  "docNum": 1002
}
```

---

### POST /stock-transfers/requests

Crea una solicitud de transferencia de stock (`InventoryTransferRequests`) sin moverla.  
Mismo cuerpo y respuesta que `POST /stock-transfers`.

---

## Entradas de Mercancía (Goods Receipts)

### POST /goods-receipts

Crea una recepción de mercancía de proveedor (`PurchaseDeliveryNotes`) en SAP.

**Cuerpo del request**

```json
{
  "vendorCode": "P001",
  "docDate": "2024-06-15",
  "comments": "Recepción factura 9872",
  "lines": [
    {
      "itemCode": "ITEM-001",
      "quantity": 100.0,
      "unitPrice": 12.50,
      "warehouseCode": "WH01"
    }
  ]
}
```

| Campo | Tipo | Requerido | Validación |
|---|---|---|---|
| `vendorCode` | string | ✓ | NotEmpty, max 15 chars |
| `docDate` | DateOnly | ✓ | |
| `lines[].unitPrice` | decimal | ✓ | ≥ 0 |
| `lines[].warehouseCode` | string | ✓ | NotEmpty, max 8 chars |

**Respuesta exitosa — 201 Created**

```json
{
  "docEntry": 55,
  "docNum": 300
}
```

---

## Depósitos (Deposits)

### POST /deposits

Crea una entrada de inventario genérica (`InventoryGenEntries`) en SAP.  
Soporta dos entornos (`Production` / `Test`) y dos tipos de depósito (`MT` / `MP`).

**Cuerpo del request**

```json
{
  "type": "MT",
  "environment": "Production",
  "docDate": "2024-06-15",
  "comments": null,
  "lines": [
    {
      "itemCode": "ITEM-001",
      "quantity": 20.0,
      "warehouseCode": "WH01"
    }
  ]
}
```

| Campo | Tipo | Valores válidos |
|---|---|---|
| `type` | enum | `MT`, `MP` |
| `environment` | enum | `Production`, `Test` |

El campo `environment` controla qué `CompanyDb` se usa (`Sap:CompanyDb` o `Sap:TestCompanyDb`).

**Respuesta exitosa — 201 Created**

```json
{
  "docEntry": 70,
  "docNum": 205
}
```

---

## Liquidaciones

### POST /liquidaciones

Crea una salida de inventario genérica (`InventoryGenExits`) en SAP.  
Mismo cuerpo que Depósitos sin los campos `type` y `environment`.

**Cuerpo del request**

```json
{
  "docDate": "2024-06-15",
  "comments": "Merma mensual",
  "lines": [
    {
      "itemCode": "ITEM-001",
      "quantity": 3.0,
      "warehouseCode": "WH01"
    }
  ]
}
```

**Respuesta exitosa — 201 Created**

```json
{
  "docEntry": 88,
  "docNum": 412
}
```

---

## Stock disponible (SQL Server)

### GET /stock/available — *pendiente de implementar*

> **Estado**: la capa de infraestructura (`SqlRepository.GetAvailableStockAsync`) y la interfaz de aplicación (`ISqlRepository`) están implementadas, pero el use case y el controller aún no existen. Este endpoint no está disponible en la versión actual.

Cuando se implemente, consultará el stock disponible por ítem y almacén directamente contra SQL Server (stored procedure `sp_GetAvailableStock`), sin pasar por SAP Service Layer.

**Parámetros de query previstos**

| Parámetro | Tipo | Descripción |
|---|---|---|
| `itemCode` | string | Código del artículo |
| `warehouseCode` | string | Código del almacén |

**Checklist para completar la implementación**

- [ ] Use case `GetAvailableStockUseCase` en `Application/UseCases/Stock/`
- [ ] Controller `StockController` en `Api/Controllers/V1/`
- [ ] Request DTO con validación (NotEmpty en ambos parámetros)
- [ ] Test de integración con mock de `ISqlRepository`
- [ ] Registrar el use case en `Application/DependencyInjection.cs`

---

## Health Checks

### GET /health

Responde si el proceso está vivo (liveness).

**200 OK**
```json
{ "status": "Healthy" }
```

### GET /health/ready

Verifica que la conexión con SAP Service Layer está activa (readiness).  
Útil para Kubernetes readiness probes — el orquestador no enruta tráfico si este endpoint falla.

**200 OK** — sesión SAP activa o recuperable:
```json
{ "status": "Healthy", "entries": { "sap": { "status": "Healthy", "description": "SAP Service Layer is reachable." } } }
```

**503 Service Unavailable** — SAP no responde:
```json
{ "status": "Unhealthy", "entries": { "sap": { "status": "Unhealthy", "description": "SAP Service Layer is not reachable." } } }
```

---

## Formato de errores

Todos los errores siguen `ProblemDetails` (RFC 7807):

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Resource not found in SAP",
  "status": 404,
  "detail": "SAP Service Layer returned HTTP 404: ...",
  "correlationId": "bc8cb300db8c4f148d334d367c959d84"
}
```

El campo `correlationId` siempre está presente y permite cruzar el error con los logs del servidor.

### Errores de validación — 400 Bad Request

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "FromWarehouse": ["'From Warehouse' must not be empty."],
    "Lines": ["At least one line is required."]
  }
}
```
