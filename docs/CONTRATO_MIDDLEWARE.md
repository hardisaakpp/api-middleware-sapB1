# Contrato de consumo — mba3-consultas → middleware SAP (puerto 8011)

> Este documento formaliza lo que la app **mba3-consultas** consume hoy de este
> middleware, tras completar (2026-07-10 a 2026-07-14) la migración de sus consultas
> puntuales a SAP desde OPENQUERY directo a HANA hacia este middleware (OPENQUERY queda
> solo como fallback automático si algún eslabón de la cadena middleware→Service Layer
> falla, no como ruta primaria).
>
> **Cualquier cambio de nombre de campo, formato o semántica en lo listado abajo rompe
> mba3-consultas y debe coordinarse antes de desplegar.** Los campos y ejemplos fueron
> verificados en vivo contra SAP real (TEST_MABEL) el 2026-07-14, no solo revisados en código.
>
> **Nota (2026-07-31):** mba3-consultas todavía no salió a producción, sigue en pruebas —
> por eso el endpoint #9 se pudo romper sin coordinar con esa app. Antes de modificar
> cualquier contrato de este middleware, confirmar con el equipo dueño del módulo que lo
> consume (o revisar el módulo) en vez de asumir que sigue siendo mba3-consultas. Otros
> endpoints en producción de este middleware, fuera de este documento: `Liquidaciones`
> (módulo Caja Chica), `StockTransfer` based-on-document/direct/with-bins (módulo
> Recepción/Bodegas).

## Endpoints consumidos y campos leídos

### 1. `GET /api/v1/items/{itemCode}`

Campos leídos: `itemCode`, `itemName`, `inventoryUoM`, `purchaseItem`, `salesItem`,
`inventoryItem`, `valid`, `frozen`, `itemType`, `packCode`, `groupCode`, `groupName`,
`preferredVendorCode`.

- ⚠️ `groupName` debe ser el texto exacto de `ItemGroups.GroupName` (tabla B1 `OITB`,
  ej. `"R-MEDIAS"`) — alimenta la regla de facturación de medias.
- ⚠️ `inventoryUoM` viene de `InventoryUOM` (string legible, ej. `"UNIDAD"`), **no** de
  `InventoryUoMEntry` (id numérico interno de SL, ej. `-1`, no es un código legible). Este
  fue un bug real hasta el 2026-07-14: el campo siempre devolvía `null` porque el
  middleware leía el campo numérico esperando un string.
- Semántica: `404` = "el ítem no existe" (respuesta definitiva; la app **no** hace
  fallback a OPENQUERY en ese caso — sí lo hace si el middleware está caído/circuito
  abierto).
- Consumidores: búsqueda de SKU (cruce de productos), reasignación de SKU en línea de
  OC, enriquecimiento de detalles de pedido, info de producto para validación de
  pedidos y facturación.

### 2. `GET /api/v1/items/{itemCode}/stock/{warehouseCode}`

Campos leídos: `inStock`, `isCommitted`, `uomCode`, `locked`.

- La app calcula `stock disponible = inStock − isCommitted` (regla de negocio propia).
- `isCommitted` debe reportar el mismo valor que el batch de validación (#6) para el
  mismo par ítem+bodega — misma fuente y cálculo (`Committed` de
  `ItemWarehouseInfoCollection`).
- `uomCode` viene del mismo campo `InventoryUOM` a nivel ítem (no por-bodega). `locked`
  viene de `Locked` dentro de `ItemWarehouseInfoCollection`, por-bodega.
- ⚠️ Precisión de esquema (verificada contra Service Layer real el 2026-07-14): en la
  entidad SL `ItemWarehouseInfo` (tabla B1 `OITW`) **no existe ningún campo `UomCode`**
  — se inspeccionaron los ~65 campos reales de una fila y no aparece. El código
  OPENQUERY original probablemente hacía un `JOIN` en HANA entre `OITW` (bodega:
  `InStock`/`Committed`/`Locked`) y `OITM` (ítem: UoM), lo que aplanaba ambos orígenes
  en una sola fila y daba la impresión de que `UomCode` vivía "en la misma colección"
  que `Locked`. En realidad es un dato a nivel ítem, replicado por bodega — que es
  exactamente cómo lo resuelve el middleware. El valor final es el mismo; solo cambia
  dónde vive estructuralmente en SAP.
- Semántica: `404` = "ítem no configurado en esa bodega" (la app lo trata como error de
  datos maestros, no de conexión).

### 3. `GET /api/v1/business-partners/{cardCode}`

Campos leídos: `cardCode`, `cardName`, `cardType`, `valid`, `frozen`. `404` = proveedor
no existe (definitivo).

### 4. `GET /api/v1/warehouses`

Respuesta: `{"warehouses": [{"whsCode", "whsName"}]}` — catálogo completo (305 bodegas
en TEST_MABEL, paginado internamente vía `odata.nextLink`, transparente al cliente).

### 5. `POST /api/v1/warehouses/validate`

Body: `{"warehouseCodes": [...]}` — la app respeta el límite de 200 códigos por request
(particiona en chunks si excede). Respuesta:
`{"warehouses": [{"whsCode", "exists", "whsName", "errorType"}]}`.

### 6. `POST /api/v1/validations/purchase-order`

Body: `{"cardCode": opcional, "lines": [{"itemCode", "warehouseCode"}]}` — la app envía
máximo 500 pares por request. Campos leídos por ítem: `itemCode`, `warehouseCode`,
`exists`, `itemName`, `purchaseItem`, `valid`, `frozen`, `warehouseConfigured`,
`inStock`, `isCommitted`, `uomCode`, `locked`, `errorType`, `errorMessage`. También lee
el bloque `businessPartner`.

### 7. `POST /api/v1/items/batch`

Body: `{"itemCodes": [...]}` — hasta 100 códigos. Cada elemento de `items` tiene el
mismo esquema que el endpoint #1 (incluye `groupName`, `inventoryUoM`). Los no
encontrados van en `notFound`, sin tumbar el batch. **Sin resultados parciales
silenciosos**: fallo de Service Layer a mitad del batch tumba el request completo con
5xx (`503` circuito/throttle, `504` timeout, `502` error de SL) — el cliente hace
fallback a OPENQUERY ante esos códigos, no ante un `notFound`.

### 8. `POST /api/v1/purchase-orders`

Body camelCase: `{"cardCode", "docDate", "comments", "u_LOTE", "lines": [...]}`. Éxito
(200/201): la app lee `docEntry` y `docNum`.

### 9. `POST /api/v1/stock-transfers/requests`

> ⚠️ Este endpoint NO lo consume mba3-consultas — lo consume el módulo de **reposición de
> mercadería**. Contrato actualizado el 2026-07-31; coordinar con ese módulo antes de
> volver a tocarlo.

Body: `{"fromWarehouse", "toWarehouse", "u_ORIGEN", "comments", "series", "stockTransferLines":
[{"itemCode", "quantity", "warehouseCode"}]}`. `docDate` ya no se recibe del cliente (el
middleware lo completa con la fecha actual). Éxito: `docEntry`, `docNum`.

### 10. `GET /api/v1/inventory-transfer-requests?commentsContain={texto}`

Campos leídos por ITR: `docNum`, `docEntry`, `toWarehouse`, `fromWarehouse`,
`comments`.

⚠️ Crítico: la app usa esto para prevenir ITRs duplicadas en reintentos — el filtro por
`comments` + los campos de bodega son su clave de negocio.

## Contrato de errores (transversal)

Los errores llegan como `ProblemDetails` RFC 7807
(`{"type", "title", "status", "detail", "correlationId"}`). Cuando el error se origina
en SAP, el middleware embebe el JSON crudo de SAP B1 como texto dentro de `detail`
(ej. `"SAP Service Layer returned HTTP 404: {\"error\": {\"code\": -2028, ...}}"` — ver
`SapException.cs` y `GlobalExceptionMiddleware.cs`).

La app parsea ese texto para recuperar `error.code` y aplicar su política de reintentos
por código SAP B1 (ej. `-2038` fuerza reintento, `-2055` lo evita). **No cambiar ese
formato sin coordinar.** Si en el futuro se prefiere estructurarlo (ej. un campo
`sapError` con el objeto ya parseado), avisar antes para adaptar el cliente.

## Arquitectura acordada

El middleware es el único camino de acceso a SAP (Service Layer + resiliencia:
sesión, throttle, retry, circuit breaker). Cada app consumidora mantiene su propio
fallback a OPENQUERY detrás de un flag, para cubrir cualquier eslabón caído de la
cadena middleware→Service Layer — OPENQUERY directo ya no se usa salvo excepción
documentada (reportes con joins que Service Layer no resuelve bien).

## Historial de verificación

- 2026-07-08: batch de prevalidación de ODC, `GET /warehouses`,
  `POST /warehouses/validate`, `GET /items/{itemCode}` extendido — implementados y
  verificados en vivo.
- 2026-07-10: `groupName` + `POST /items/batch` — implementados y verificados en vivo.
- 2026-07-14: `isCommitted` en el endpoint individual de stock (bug de producción,
  faltaba desde su implementación) y `uomCode`/`locked` en ese mismo endpoint —
  implementados. En el proceso se detectó y corrigió un bug preexistente: `inventoryUoM`
  (y su equivalente `uomCode` del batch) leía `InventoryUoMEntry` (numérico) en vez de
  `InventoryUOM` (string legible), por lo que siempre devolvía `null`. Corregido en los
  tres puntos que lo usaban (`GetItemAsync`, `GetItemsForValidationAsync`,
  `GetItemStockAsync`) y verificado en vivo: `R215.810.NO` → `"UNIDAD"` idéntico en los
  tres endpoints.
