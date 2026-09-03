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
>
> **Nota (2026-08-31, merge feature/middleware-odc-excel ↔ test):** el endpoint #9 volvió a
> tener 2 consumidores simultáneos: mba3-consultas (vía `SAP_USE_MIDDLEWARE`, aún no en
> producción) y reposición de mercadería. Se combinó el contrato de ambos (ver detalle en
> el endpoint #9) en vez de que uno pisara al otro. Combinado localmente contra fixtures;
> **pendiente de verificar en vivo** contra SAP real, igual que cualquier cambio de
> contrato nuevo en este documento.

## Endpoints consumidos y campos leídos

### 1. `GET /api/v1/items/{itemCode}`

Campos leídos: `itemCode`, `itemName`, `inventoryUoM`, `purchaseItem`, `salesItem`,
`inventoryItem`, `valid`, `frozen`, `itemType`, `packCode`, `groupCode`, `groupName`,
`preferredVendorCode`, `pvp`, `codeBars`, `marca`.

- ⚠️ `pvp` es el precio de la lista **"Precio Minorista" (`PriceList` 1 en SAP,
  convención de negocio fija, no configurable por compañía)**. Se omite (ausente
  del JSON) si el ítem no tiene esa lista de precio configurada. Alimenta la
  validación obligatoria de Pack/PVP contra SAP (RN-02): si el PVP del Excel no
  coincide con este valor, la carga del pedido se bloquea hasta que el usuario
  elija cuál valor usar. **`ItemPrices` no es una navegación expandible en este
  SAP** — Service Layer rechaza `$expand=ItemPrices` con `-1000 "Cannot expand
  invalid navigation property"` (verificado en vivo el 2026-08-07). El middleware
  lo resuelve con una llamada aparte al sub-recurso `Items('CODE')/ItemPrices`
  por cada ítem — ver `GetPrecioMinoristaAsync` en `SapGateway.cs`. En
  `POST /items/batch` esto significa **N llamadas a SAP además de la consulta
  batch** (una por ítem resuelto, concurrentes, acotadas por el mismo
  `SapThrottle`/`MaxConcurrentSapCalls` de siempre) — en este ambiente cada
  llamada a SAP toma ~2.5s y no paraleliza bien (confirmado con llamadas
  concurrentes puras, sin el middleware de por medio), así que un batch de
  varios ítems con PVP puede añadir varios segundos de latencia. Es un costo
  real, no un bug — no hay forma de traer el precio de varios ítems en una sola
  llamada en este SAP (no existe una entidad de precios a nivel raíz que
  soporte `$filter` por código).
- ⚠️ `codeBars` viene del campo directo **`BarCode`** de la entidad `Items` (no
  de la colección `ItemBarCodeCollection`, que es por-UoM). Se usa para
  sincronizar el código de barras del pedido con el de SAP (fuente de verdad).
  El nombre del campo en SAP es `BarCode` (singular) — `CodeBars` (como se
  documentó el 2026-08-06) no existe en este SAP y su uso en `$select` era
  rechazado con el mismo error `-1000`. Ver nota de honestidad más abajo.
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

> ℹ️ Cada elemento también puede traer `warehouses` (desglose de `inStock`/`committed`/
> `locked`/`ordered` por bodega) — mba3-consultas no lo lee hoy; lo agregó api-articulo-
> maestro para su verificación de movimientos por ítem.

### 8. `POST /api/v1/purchase-orders`

Body camelCase: `{"cardCode", "docDate", "comments", "u_LOTE", "lines": [...]}`. Éxito
(200/201): la app lee `docEntry` y `docNum`.

### 9. `POST /api/v1/stock-transfers/requests`

> ⚠️ **2 consumidores simultáneos.** El principal hoy es el módulo de **reposición de
> mercadería** (contrato desde 2026-07-31: `u_ORIGEN`/`series` obligatorios,
> `stockTransferLines` con `warehouseCode` por línea). mba3-consultas también le pega a
> este mismo endpoint vía `SAP_USE_MIDDLEWARE` (aún no en producción — `srv-mabuiosapi`
> sigue con el cliente directo a SAP), mandando `lines` con `uOdc`/`projectCode`
> opcionales y **sin `u_ORIGEN`**. Coordinar con ambos equipos antes de volver a tocar
> este contrato.

Body combinado: `{"fromWarehouse", "toWarehouse", "docDate", "comments", "lines":
[{"itemCode", "quantity", "projectCode", "warehouseCode"}], "uOdc", "series", "u_ORIGEN"}`.
Éxito: `docEntry`, `docNum`.

- `series` y `u_ORIGEN` son **requeridos** (400 si faltan) — es lo que necesita
  reposición de mercadería. Si mba3-consultas llega a ejercitar este endpoint con
  `SAP_USE_MIDDLEWARE=true` tal como está hoy (sin mandar `u_ORIGEN`), va a recibir 400 —
  pendiente actualizar ese cliente antes de activar la bandera contra este middleware.
- `uOdc` (nº de OC de origen, UDF `U_ODC`), `projectCode` (por línea) y `warehouseCode`
  (por línea, destino si difiere del `toWarehouse` del encabezado) son opcionales; se
  omiten del payload a SAP si vienen null.
- `docDate` sigue viniendo del cliente (a diferencia de las variantes `based-on-document`/
  `direct`/`with-bins`, donde el middleware la completa con la fecha actual).

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
- 2026-08-06: `pvp` y `codeBars` en `GET /items/{itemCode}` y `POST /items/batch` — bug
  real detectado en uso: la validación de Pack/PVP contra SAP (RN-02) en mba3-consultas
  nunca podía marcar una discrepancia de PVP porque el middleware jamás pedía precio ni
  código de barras a Service Layer (`$select` no los incluía, `ItemResponse` no tenía
  esas propiedades). Se agregó `$expand=ItemPrices` a la consulta de ítem y se extrae
  la entrada de `PriceList` 1 ("Precio Minorista"); `codeBars` se agregó al `$select`
  existente (campo directo de `Items`, no requiere `$expand`). Pendiente de verificar en
  vivo contra SAP real (los tests usan fixtures, no una consulta real a TEST_MABEL).
- **2026-08-07 — corrección de lo anterior, esta vez sí verificado en vivo contra
  TEST_MABEL real (no solo fixtures):** el cambio del 2026-08-06 nunca llegó a
  funcionar contra SAP real — ambos campos fallaban, y no se detectó antes porque
  mba3-consultas leía valores de caché poblados por el fallback OPENQUERY (que sí
  funcionaba) y eso se confundió con éxito del middleware. Dos bugs distintos,
  ambos de asunciones sobre el esquema de SAP sin verificar contra la instancia
  real:
  - `$expand=ItemPrices` es rechazado por este Service Layer con
    `-1000 "Cannot expand invalid navigation property 'ItemPrices' for entity
    type 'Item'"`, tanto en la consulta individual como en la de batch, tanto
    para ítems existentes como inexistentes (100% reproducible, no era
    intermitente). `ItemPrices` en este SAP solo es accesible como sub-recurso
    directo (`Items('CODE')/ItemPrices`, sin `$expand` y sin soporte de
    `$filter`). Corregido: `GetPrecioMinoristaAsync` hace esa llamada aparte,
    por ítem; en el batch son N llamadas concurrentes (ver nota de rendimiento
    arriba, en la sección del endpoint).
  - `CodeBars` no existe como propiedad de `Item` en este SAP — rechazado con el
    mismo `-1000`. El campo real es **`BarCode`** (singular). El nombre de la
    propiedad JSON externa de este middleware (`codeBars`) no cambió, solo la
    consulta interna a SAP.
  - Verificado en vivo: `GET /items/{code}` y `POST /items/batch` contra
    TEST_MABEL real, con ítems existentes e inexistentes, y con un ítem cuya
    consulta de precio falla a propósito (para confirmar que no tumba el resto
    del batch). 39 tests de integración + 29 unitarios, todos contra este
    cambio.
- 2026-08-31: merge de `feature/middleware-odc-excel` con `test` — se combinó el
  contrato del endpoint #9 (ver nota arriba) y se sumaron a esta rama las
  funcionalidades que `test` había agregado en paralelo (CRUD de Items, Price Lists,
  variantes de Stock Transfer con bins/directo/basado-en-documento, `warehouses` en
  batch de ítems). Combinado localmente (build + 96 tests entre unitarios e
  integración, todos contra fixtures); **pendiente de verificar en vivo** contra SAP
  real, en particular el contrato combinado del endpoint #9.
