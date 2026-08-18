# Referencia de API

Base URL: `http://localhost:8011/api/v1`  
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

`POST /stock-transfers/based-on-document`, `POST /stock-transfers/direct` y
`POST /stock-transfers/with-bins` inician sesión en SAP contra `Sap:TestCompanyDb` /
`Sap:TestPassword` (nunca `Sap:CompanyDb`) — cuál base real es esa (test o producción) lo decide
la variable de entorno del deploy, no el código.

### POST /stock-transfers/based-on-document

Crea una transferencia de stock (`StockTransfers`) referenciando un documento base por línea
(`baseType`/`baseEntry`/`baseLine`, ej. una solicitud de transferencia u orden de compra ya
existente en SAP).

**Cuerpo del request**

```json
{
  "docObjectCode": "67",
  "cardCode": null,
  "docDate": "2024-06-15",
  "taxDate": null,
  "comments": "Reposición mensual",
  "fromWarehouse": "WH01",
  "toWarehouse": "WH02",
  "u_ORIGEN": null,
  "series": null,
  "priceList": null,
  "stockTransferLines": [
    {
      "baseType": 1250000001,
      "baseEntry": 123,
      "baseLine": 0,
      "itemCode": "ITEM-001",
      "quantity": 5.0,
      "warehouseCode": "WH02"
    }
  ]
}
```

| Campo | Tipo | Requerido | Validación |
|---|---|---|---|
| `docObjectCode` | string | — | Opcional |
| `cardCode` | string | — | Opcional |
| `docDate` | string | ✓ | NotEmpty, formato `yyyy-MM-dd` |
| `taxDate` | string | — | Opcional |
| `comments` | string | — | Opcional |
| `fromWarehouse` | string | ✓ | NotEmpty, max 8 chars, distinto de `toWarehouse` |
| `toWarehouse` | string | ✓ | NotEmpty, max 8 chars |
| `u_ORIGEN` | string | — | Opcional |
| `series` | int | — | Opcional |
| `priceList` | int | — | Opcional (default SAP: -2, "Último precio determinado") |
| `stockTransferLines` | array | ✓ | Al menos un elemento |
| `stockTransferLines[].baseType` | int | — | Opcional |
| `stockTransferLines[].baseEntry` | int | — | Opcional |
| `stockTransferLines[].baseLine` | int | — | Opcional |
| `stockTransferLines[].itemCode` | string | ✓ | NotEmpty, max 20 chars |
| `stockTransferLines[].quantity` | decimal | ✓ | > 0 |
| `stockTransferLines[].warehouseCode` | string | ✓ | NotEmpty, max 8 chars |

**Respuesta exitosa — 201 Created**

```json
{
  "docEntry": 42,
  "docNum": 1001
}
```

---

### POST /stock-transfers/direct

Crea una transferencia de stock (`StockTransfers`) directa entre almacenes, sin referenciar
ningún documento base. Mismo cuerpo que `/based-on-document` pero sin `baseType`/`baseEntry`/
`baseLine` por línea.

**Cuerpo del request**

```json
{
  "docObjectCode": null,
  "cardCode": null,
  "docDate": "2024-06-15",
  "taxDate": null,
  "comments": "Reposición mensual",
  "fromWarehouse": "WH01",
  "toWarehouse": "WH02",
  "u_ORIGEN": null,
  "series": null,
  "priceList": null,
  "stockTransferLines": [
    {
      "itemCode": "ITEM-001",
      "quantity": 5.0,
      "warehouseCode": "WH02"
    }
  ],
  "u_sap_id": null,
  "u_GUIA_LAAR": null
}
```

Mismas validaciones que `/based-on-document` (menos las de `baseType`/`baseEntry`/`baseLine`,
que no existen aquí).

| Campo | Tipo | Requerido | Validación |
|---|---|---|---|
| `u_sap_id` | string | — | Opcional. Clave de correlación del sistema consumidor, usada para idempotencia (ver abajo) |
| `u_GUIA_LAAR` | string | — | Opcional. UDF de guía de transporte del consumidor |

**Idempotencia con `u_sap_id`**: si se envía `u_sap_id` y el `POST` a SAP agota el timeout
(`TimeoutRejectedException`), el middleware consulta `StockTransfers` filtrando por ese
`u_sap_id` antes de relanzar el error — si SAP sí llegó a crear el documento a pesar del
timeout del cliente, se recupera y se devuelve ese `DocEntry`/`DocNum` en lugar de fallar.
Este chequeo **solo se dispara ante timeout**, no en cada request — si SAP responde a tiempo
(el caso normal), no hay verificación adicional ni riesgo de que un reenvío con el mismo
`u_sap_id` en condiciones normales sea detectado como duplicado: cada `POST` exitoso crea un
documento nuevo. Para saber si una transferencia con un `u_sap_id` dado ya existe antes de
reenviarla, usar `GET /stock-transfers/by-correlation/{uSapId}` (ver abajo).

**Respuesta exitosa — 201 Created**

```json
{
  "docEntry": 45,
  "docNum": 1004
}
```

---

### GET /stock-transfers/by-correlation/{uSapId}

Busca una transferencia de stock (`StockTransfers`) por su UDF de correlación `u_sap_id`.
Pensado para que un sistema consumidor verifique si una transferencia ya fue creada en SAP
antes de reenviarla (por ejemplo, tras un timeout o reintento de su propio lado). Es una
lectura síncrona y directa contra SAP — no pasa por el sistema de jobs/cola.

**Respuesta exitosa — 200 OK**

```json
{
  "docEntry": 45,
  "docNum": 1004
}
```

| Código | Motivo |
|---|---|
| `400 Bad Request` | `uSapId` vacío |
| `404 Not Found` | No existe ningún `StockTransfer` con ese `u_sap_id` en SAP |
| `502 Bad Gateway` | SAP devolvió un error inesperado |

---

### POST /stock-transfers/with-bins

Crea una transferencia de stock (`StockTransfers`) con asignación de ubicaciones (bins) por
línea. Cubre los 4 combos posibles de bodega origen/destino con o sin ubicaciones habilitadas:
el array `stockTransferLinesBinAllocations` es opcional y por línea, así que si un lado no
maneja bins simplemente no se le manda ninguna entrada (puede traer solo entradas de un lado,
de ambos, o ninguna).

**Cuerpo del request**

```json
{
  "docObjectCode": null,
  "cardCode": null,
  "docDate": "2024-06-15",
  "taxDate": null,
  "comments": "Reposición mensual",
  "fromWarehouse": "WH01",
  "toWarehouse": "WH02",
  "u_ORIGEN": null,
  "series": null,
  "priceList": null,
  "stockTransferLines": [
    {
      "baseType": null,
      "baseEntry": null,
      "baseLine": null,
      "itemCode": "ITEM-001",
      "quantity": 10.0,
      "warehouseCode": "WH02",
      "stockTransferLinesBinAllocations": [
        { "binAbsEntry": 5, "quantity": 10.0, "binActionType": 2 },
        { "binAbsEntry": 9, "quantity": 10.0, "binActionType": 1 }
      ]
    }
  ]
}
```

| Campo | Tipo | Requerido | Validación |
|---|---|---|---|
| `docObjectCode`, `cardCode`, `taxDate`, `u_ORIGEN`, `series`, `priceList` | — | — | Opcionales, igual que en `/based-on-document` |
| `docDate` | string | ✓ | NotEmpty, formato `yyyy-MM-dd` |
| `fromWarehouse` / `toWarehouse` | string | ✓ | NotEmpty, max 8 chars, distintos entre sí |
| `stockTransferLines` | array | ✓ | Al menos un elemento |
| `stockTransferLines[].baseType/baseEntry/baseLine` | int | — | Opcionales (referencia a documento base) |
| `stockTransferLines[].itemCode` | string | ✓ | NotEmpty, max 20 chars |
| `stockTransferLines[].quantity` | decimal | ✓ | > 0 |
| `stockTransferLines[].warehouseCode` | string | ✓ | NotEmpty, max 8 chars |
| `stockTransferLines[].stockTransferLinesBinAllocations` | array | — | Opcional; omitir si esa bodega no tiene bins |
| `...binAllocations[].binAbsEntry` | int | ✓ (si se manda el array) | > 0 |
| `...binAllocations[].quantity` | decimal | ✓ (si se manda el array) | > 0 |
| `...binAllocations[].binActionType` | int | ✓ (si se manda el array) | `1` = Entrada (bin destino) o `2` = Salida (bin origen) — se mapea a `batToWarehouse`/`batFromWarehouse` de SAP |

**Respuesta exitosa — 201 Created**

```json
{
  "docEntry": 46,
  "docNum": 1005
}
```

---

### POST /stock-transfers/requests

Crea una solicitud de transferencia de stock (`InventoryTransferRequests`) sin moverla. Inicia
sesión contra `Sap:CompanyDb` (producción) — **consumido en vivo por el módulo de reposición de
mercadería, no cambiar su contrato sin coordinar con ese módulo** (ver `docs/CONTRATO_MIDDLEWARE.md`).

**Cuerpo del request**

```json
{
  "fromWarehouse": "OUT-CD",
  "toWarehouse": "TOUT-BMB",
  "u_ORIGEN": "C",
  "comments": "CUIDADO ORAL",
  "series": 10133,
  "stockTransferLines": [
    {
      "itemCode": "SKBLZ0147",
      "quantity": 24.0,
      "warehouseCode": "TOUT-BMB"
    }
  ]
}
```

`docDate` ya no se recibe del cliente — el middleware lo completa internamente con la fecha
actual al armar el payload hacia SAP.

| Campo | Tipo | Requerido | Validación |
|---|---|---|---|
| `fromWarehouse` | string | ✓ | NotEmpty, max 8 chars, distinto de `toWarehouse` |
| `toWarehouse` | string | ✓ | NotEmpty, max 8 chars |
| `u_ORIGEN` | string | ✓ | NotEmpty |
| `comments` | string | — | Opcional |
| `series` | int | ✓ | > 0 |
| `stockTransferLines` | array | ✓ | Al menos un elemento |
| `stockTransferLines[].itemCode` | string | ✓ | NotEmpty, max 20 chars |
| `stockTransferLines[].quantity` | decimal | ✓ | > 0 |
| `stockTransferLines[].warehouseCode` | string | ✓ | NotEmpty, max 8 chars |

**Respuesta exitosa — 201 Created**

```json
{
  "docEntry": 43,
  "docNum": 1002
}
```

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

## Artículos (Items)

### GET /items/{itemCode}

Obtiene la ficha del artículo desde SAP, incluyendo los campos que consume la búsqueda
por SKU exacto (pack, grupo y proveedor preferido).

**Respuesta exitosa — 200 OK**

```json
{
  "itemCode": "R190.1012",
  "itemName": "CASUAL C. CASUAL LISA DISEÑO SURTIDO X1",
  "inventoryUoM": "UNIDAD",
  "onHand": 822.0,
  "purchaseItem": true,
  "salesItem": true,
  "inventoryItem": true,
  "valid": true,
  "frozen": false,
  "itemType": "itItems",
  "packCode": "X1",
  "groupCode": 103,
  "groupName": "R-MEDIAS",
  "preferredVendorCode": "P1790389219001"
}
```

| Campo | Origen SL | Notas |
|---|---|---|
| `inventoryUoM` | `InventoryUOM` | Código de unidad de medida legible (ej. `"UNIDAD"`). **No** es `InventoryUoMEntry` (id numérico interno de SL, no legible) — error corregido el 2026-07-14, antes este campo siempre devolvía `null` |
| `packCode` | `U_IXX_PACK` (UDF) | `null` si el ítem no lo tiene. Si la base no tiene el UDF, el middleware reintenta el `$select` sin él y devuelve `null` |
| `groupCode` | `ItemsGroupCode` | `null` si SL no lo devuelve |
| `groupName` | `ItemGroups.GroupName` | Resuelto vía cache en memoria del mapa código→nombre (TTL `Sap:ItemGroupsCacheMinutes`, default 360). `null` si el grupo no se puede resolver; siempre coherente con `groupCode` |
| `preferredVendorCode` | `Mainsupplier` | `null` si el ítem no tiene proveedor preferido (`""` de SL se normaliza a `null`) |

Los campos con valor `null` se omiten del JSON (política global de la API).

**Códigos de error**: `404` si el `itemCode` no existe en SAP.

---

### POST /items/batch

Ficha completa de hasta 100 artículos en **una sola llamada** (reemplaza N llamadas a
`GET /items/{itemCode}`). Cada elemento de `items` es el mismo objeto que devuelve el
endpoint individual (incluye `groupName`). Solo lectura e idempotente.

**Cuerpo del request**

```json
{ "itemCodes": ["R215.810.NO", "780-2404174", "NO-EXISTE-123"] }
```

| Campo | Tipo | Requerido | Validación |
|---|---|---|---|
| `itemCodes` | array | ✓ | 0 a 100 códigos, cada uno NotEmpty max 50 chars. Repetidos se deduplican. `[]` devuelve 200 con listas vacías |

**Respuesta exitosa — 200 OK**

```json
{
  "items": [
    { "itemCode": "R215.810.NO", "itemName": "...", "groupCode": 103, "groupName": "R-MEDIAS", "...": "..." }
  ],
  "notFound": ["NO-EXISTE-123"]
}
```

- Los códigos que no existen en SAP van en `notFound` — no provocan error del batch.
- El **orden de `items` no está garantizado**; mapear por `itemCode`.
- **Sin resultados parciales silenciosos**: si Service Layer falla a mitad del batch, el
  request completo falla con problem-detail 5xx (`503` circuito abierto/throttle,
  `504` timeout, `502` error de SL) para que el cliente haga fallback.
- Internamente consulta SL en sub-chunks (`Validation:SapChunkSize`, paralelismo
  `Validation:MaxParallelChunks`), transparente para el cliente.

**Códigos de error**

| Status | Cuándo |
|---|---|
| `400 Bad Request` | Payload inválido o más de 100 códigos |
| `502 / 503 / 504` | Fallo de Service Layer durante el batch (ver arriba) — sin parciales |

---

### GET /items/{itemCode}/stock/{warehouseCode}

Stock del artículo en una bodega.

**Respuesta exitosa — 200 OK**

```json
{
  "itemCode": "R215.810.NO",
  "warehouseCode": "RL-CD",
  "inStock": 0.0,
  "isCommitted": 292.0,
  "uomCode": "UNIDAD",
  "locked": false
}
```

- `inStock` es el OnHand crudo de la bodega (sin descontar nada).
- `isCommitted` es la cantidad comprometida/reservada — misma fuente y cálculo que el campo
  homónimo de `POST /validations/purchase-order`, así que un mismo ítem+bodega da el mismo
  valor consultado por cualquiera de las dos rutas. El cliente calcula
  `disponible = inStock - isCommitted`.
- `uomCode` es la unidad de medida a nivel ítem (campo SL `InventoryUOM`, ej. `"UNIDAD"`) —
  **no** confundir con `InventoryUoMEntry`, que es el id numérico interno de SL y no sirve
  como código legible. `null` si SL no la tiene poblada.
- `locked` es el flag de bloqueo por bodega (`Locked` en `ItemWarehouseInfoCollection`).
- `404` solo si el ítem no existe; bodega sin registro para el ítem devuelve `inStock`/`isCommitted`/`locked` en `0`/`false` (pero `uomCode` sigue resolviendo el valor a nivel ítem).

---

### POST /items/stock/batch

Stock de hasta 100 artículos en **una sola bodega fija**, en una sola llamada (reemplaza N
llamadas a `GET /items/{itemCode}/stock/{warehouseCode}` con el mismo `warehouseCode`). Solo
lectura, síncrono (no pasa por la cola de jobs).

**Cuerpo del request**

```json
{
  "warehouseCode": "RL-CD",
  "itemCodes": ["R215.810.NO", "780-2404174", "NO-EXISTE-123"]
}
```

| Campo | Tipo | Requerido | Validación |
|---|---|---|---|
| `warehouseCode` | string | ✓ | NotEmpty, max 8 chars — aplica a todos los ítems de la llamada |
| `itemCodes` | array | ✓ | 0 a 100 códigos, cada uno NotEmpty max 50 chars. Repetidos se deduplican. `[]` devuelve 200 con listas vacías |

**Respuesta exitosa — 200 OK**

```json
{
  "items": [
    { "itemCode": "R215.810.NO", "warehouseCode": "RL-CD", "inStock": 0.0, "isCommitted": 292.0, "uomCode": "UNIDAD", "locked": false }
  ],
  "notFound": ["NO-EXISTE-123", "780-2404174"]
}
```

- Cada elemento de `items` es el mismo objeto que devuelve `GET /items/{itemCode}/stock/{warehouseCode}`
  (mismos campos, misma fuente/cálculo de `inStock`/`isCommitted`).
- A diferencia del endpoint individual, aquí **no hay distinción entre "ítem no existe" y
  "bodega sin registro para el ítem"** — ambos casos caen en `notFound`, sin ceros falsos.
  El **orden de `items` no está garantizado**; mapear por `itemCode`.
- **Sin resultados parciales silenciosos**: un fallo de Service Layer a mitad del batch tumba
  el request completo con problem-detail 5xx (mismo comportamiento que `POST /items/batch`).
- Internamente reutiliza la misma consulta que `POST /validations/purchase-order` (trae
  `ItemWarehouseInfoCollection` completa por ítem y filtra en memoria por `warehouseCode`,
  igual que hace el endpoint individual de stock) — no hay `$filter` de SAP anidado en la
  colección de bodegas, es una limitación de Service Layer, no del middleware.

**Códigos de error**

| Status | Cuándo |
|---|---|
| `400 Bad Request` | Payload inválido, `warehouseCode` vacío, o más de 100 códigos |
| `502 / 503 / 504` | Fallo de Service Layer durante el batch — sin parciales |

---

## Bodegas (Warehouses)

### GET /warehouses

Devuelve el catálogo completo de bodegas de SAP (reemplaza la consulta OPENQUERY sobre
`OWHS`). Sigue internamente el paging de Service Layer (`odata.nextLink`), así que
devuelve todas las bodegas sin importar el tamaño del catálogo.

**Respuesta exitosa — 200 OK**

```json
{
  "warehouses": [
    { "whsCode": "RL-CD", "whsName": "ROLAND CENTRO DE DISTRIBUCION" },
    { "whsCode": "LP-CD", "whsName": "LILI PINK CENTRO DE DISTRIBUCION" }
  ]
}
```

> Nota: en Service Layer los campos se llaman `WarehouseCode`/`WarehouseName`; se mapean
> a `whsCode`/`whsName` por consistencia con el contrato que consumen los clientes.

---

### POST /warehouses/validate

Valida una lista de códigos de bodega en una sola llamada (reemplaza el bucle N+1 de
validación código por código). Solo lectura e idempotente.

**Cuerpo del request**

```json
{ "warehouseCodes": ["RL-CD", "TIENDA01", "NOEXISTE"] }
```

| Campo | Tipo | Requerido | Validación |
|---|---|---|---|
| `warehouseCodes` | array | ✓ | 1 a 200 códigos, cada uno NotEmpty max 8 chars. Repetidos se deduplican: un resultado por código único |

**Respuesta exitosa — 200 OK** (siempre 200 si el batch se procesó; problemas por-elemento en `errorType`)

```json
{
  "warehouses": [
    { "whsCode": "RL-CD", "exists": true, "whsName": "ROLAND CENTRO DE DISTRIBUCION", "errorType": null },
    { "whsCode": "NOEXISTE", "exists": false, "whsName": null, "errorType": "not_found" }
  ]
}
```

`errorType`: `null` | `"not_found"` | `"connection_error"` | `"error"` (mismo catálogo que
`POST /validations/purchase-order`; los `null` se omiten del JSON). Fallos parciales de un
lote interno no tumban el batch: solo esos códigos salen con `connection_error`.

**Códigos de error**

| Status | Cuándo |
|---|---|
| `400 Bad Request` | Payload inválido o más de 200 códigos |
| `503 Service Unavailable` | Ningún código pudo consultarse (Service Layer totalmente caído) |

Usa la misma configuración `Validation` (chunks, paralelismo, timeout) que el batch de ODC.

---

## Validaciones (Batch)

### POST /validations/purchase-order

Valida una orden de compra completa (proveedor + líneas ítem/bodega) en **una sola llamada**,
reemplazando el bucle de `GET /items/{x}` + `GET /items/{x}/stock/{wh}` + `GET /business-partners/{cc}`
por línea. Solo lectura e idempotente: no crea nada en SAP.

Internamente consulta Service Layer en lotes (`$filter` con hasta `Validation:SapChunkSize`
códigos por query, paralelismo `Validation:MaxParallelChunks`, timeout total
`Validation:BatchTimeoutSeconds`). Un fallo parcial de un lote **no tumba el batch**: solo
esos elementos salen con `errorType = "connection_error"`.

**Cuerpo del request**

```json
{
  "cardCode": "P1790389219001",
  "lines": [
    { "itemCode": "5986.NG.8", "warehouseCode": "RL-CD" },
    { "itemCode": "5656.8",    "warehouseCode": "RL-CD" }
  ]
}
```

| Campo | Tipo | Requerido | Validación |
|---|---|---|---|
| `cardCode` | string | — | Si viene, se valida el proveedor. Max 15 chars |
| `lines` | array | ✓ | 1 a 500 elementos. Pares `(itemCode, warehouseCode)` repetidos se deduplican: un resultado por par único |
| `lines[].itemCode` | string | ✓ | NotEmpty, max 50 chars |
| `lines[].warehouseCode` | string | ✓ | NotEmpty, max 8 chars |

**Respuesta exitosa — 200 OK** (siempre 200 si el batch pudo procesarse, aunque haya
elementos inválidos; los problemas van por-elemento en `errorType`)

```json
{
  "businessPartner": {
    "cardCode": "P1790389219001",
    "exists": true,
    "cardName": "PROVEEDOR X S.A.",
    "cardType": "cSupplier",
    "valid": true,
    "frozen": false,
    "errorType": null
  },
  "items": [
    {
      "itemCode": "5986.NG.8",
      "warehouseCode": "RL-CD",
      "exists": true,
      "itemName": "...",
      "purchaseItem": true,
      "valid": true,
      "frozen": false,
      "warehouseConfigured": true,
      "inStock": 120.0,
      "isCommitted": 20.0,
      "uomCode": "UNIDAD",
      "locked": false,
      "errorType": null
    }
  ]
}
```

Notas de semántica (idéntica a los endpoints unitarios):

- `businessPartner` es `null` si no se envió `cardCode`.
- `inStock` es el OnHand crudo de la bodega e `isCommitted` va aparte — el cliente calcula
  `disponible = inStock - isCommitted`.
- Los campos con valor `null` se omiten del JSON (política global de la API).
- Cuando `errorType != null`, los demás campos del elemento pueden venir en `null`/`false`.

**Catálogo de `errorType` por elemento**

| errorType | Significado |
|---|---|
| `item_not_found` | El `itemCode` no existe en SAP (equivale al 404 de `GET /items/{x}`) |
| `warehouse_not_found` | El ítem existe pero no está configurado en esa bodega (`warehouseConfigured=false`, `inStock=0`) |
| `not_found` | (solo businessPartner) el `cardCode` no existe en SAP |
| `connection_error` | No se pudo consultar ese elemento en Service Layer (fallo parcial o timeout del batch) |
| `error` | Otro error de SL para ese elemento; incluye `errorMessage` |
| `null` | Consulta exitosa — los flags (`exists`, `purchaseItem`, `frozen`, …) dicen si es válido; esa regla la evalúa el cliente |

**Códigos de error del batch completo**

| Status | Cuándo |
|---|---|
| `400 Bad Request` | Payload inválido o más de 500 líneas |
| `503 Service Unavailable` | Ningún elemento pudo consultarse (Service Layer totalmente inaccesible) |

**Configuración** (`appsettings.json`, sección `Validation`)

| Clave | Default | Descripción |
|---|---|---|
| `BatchTimeoutSeconds` | 60 | Deadline total del batch; lo no consultado sale como `connection_error` |
| `SapChunkSize` | 20 | Códigos de ítem por query `$filter` a SL (mantener ≤ 20, page size de SL) |
| `MaxParallelChunks` | 4 | Queries de lote simultáneas (el throttle global de SAP sigue aplicando) |

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
