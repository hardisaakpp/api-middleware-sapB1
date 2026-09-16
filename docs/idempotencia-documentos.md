# Idempotencia en la creación de documentos SAP: convención, funcionamiento y plan

Versión para revisión del equipo, 14/09/2026. Origen: incidente del 10/09/2026, 5 jobs de
`POST /stock-transfers/based-on-document` en `ERROR "SAP saturado"` con la transferencia ya creada
en SAP (ver [job-queue.md](job-queue.md#reintentos-y-documentos-duplicados)).

## Objetivo

Que un reintento, venga de la cola del middleware o del módulo consumidor, **nunca cree un segundo
documento en SAP**: si el documento ya existe se trabaja sobre él, si no existe se crea. El campo
es **opcional**: un módulo que no lo envíe sigue funcionando exactamente como hoy.

## Cómo funciona con la clave

```
POST con u_sap_id
  1. ¿Existe en SAP un documento con U_sap_id = clave?
       sí, y es el mismo pedido                → devolver ese DocEntry/DocNum, recovered = true, no crear
       sí, pero es otro pedido                 → 409 Conflict, no crear
       no                                      → 2
  2. POST a SAP
       201 → devolver, recovered = false
       falla sin respuesta clara (timeout, corte, 400 "base ya consumida", 500) → volver a buscar
             encontrado    → devolver como éxito, recovered = true (SAP lo creó aunque no respondió)
             no encontrado → error transitorio → la cola reintenta desde el paso 1
```

Cada reintento arranca por la búsqueda, así que como máximo hay **un** documento por clave. Si se
agotan los 5 reintentos el job queda en `ERROR` con el último error real, y cuando el módulo vuelve
a enviar con la misma clave el middleware encuentra el documento si SAP lo creó, o lo crea si no.

"Mismo pedido" se define por documento, no por líneas ni cantidades, porque un reintento puede traer
líneas distintas (recepción omite las de cantidad cero) y el documento de SAP ya no admite cambios:

| Documento | Mismo pedido = |
|---|---|
| Transferencias (OWTR) | mismas bodegas origen/destino y mismo documento base (`baseEntry`); sin base, mismos artículos |
| Solicitudes de traslado (OWTQ) | mismas bodegas origen/destino y mismos artículos |
| Entradas de mercancía de compras (OPDN) | mismo proveedor (`CardCode`) y misma(s) orden(es) de compra base; sin base, mismos artículos |
| Órdenes de compra (OPOR) | mismo proveedor (`CardCode`) y mismos artículos |
| Liquidaciones / facturas de proveedor (OPCH) | mismo socio (`CardCode`) y mismas cuentas contables (`AccountCode`) |
| Depósitos = entradas de mercancías de inventario (OIGN, `InventoryGenEntries`) | cada (artículo, bodega) del request está en el documento |

## Estado por endpoint (14/09/2026)

| Endpoint | Objeto SAP / compañía | Consumidor | Clave hoy | En producción (`main`) | En el árbol local |
|---|---|---|---|---|---|
| `POST /stock-transfers/based-on-document` | OWTR, TestCompanyDb | api-recepcion-mercaderia | header `Idempotency-Key: reception-transfer-{id}` | ninguna, la cabecera se ignora | completa; GUID de respaldo si no hay clave |
| `POST /stock-transfers/direct` | OWTR, TestCompanyDb | api-transferencias-directas | body `u_sap_id` = GUID mayúsculas | solo recuperación tras timeout, buscando en la compañía equivocada | búsqueda corregida; sigue sin verificación previa (pendiente acordar con su equipo) |
| `POST /stock-transfers/with-bins` | OWTR, TestCompanyDb | sin consumidor identificado | — | ninguna | completa; GUID de respaldo si no hay clave |
| `POST /stock-transfers` (for-company) | OWTR, CompanyDb | api-cedi (distribución) | — | ninguna | completa; GUID de respaldo si no hay clave |
| `POST /stock-transfers/requests` | OWTQ, CompanyDb | api-reposiciones | — | ninguna | completa; GUID de respaldo si no hay clave (UDF confirmado en OWTQ) |
| `POST /goods-receipts` | OPDN (Compras › Entrada de mercancías), CompanyDb | api-cedi | — | ninguna | completa; GUID de respaldo si no hay clave |
| `POST /deposits` | OIGN (Inventario › Entrada de mercancías), CompanyDb | sin consumidor identificado en los repos locales | — | ninguna | completa; GUID de respaldo si no hay clave |
| `POST /purchase-orders` | OPOR, CompanyDb | api-cedi | — | recuperación tras timeout por CardCode + Comments (no única) | completa; GUID de respaldo si no hay clave |
| `POST /liquidaciones` | OPCH, CompanyDb | Caja Chica (web-scnexus, `slayerApi`) | — | ninguna | completa; GUID de respaldo si no hay clave |

Todos los endpoints salvo `/direct` generan una clave (GUID) cuando el módulo no la envía, así los
reintentos de la cola quedan protegidos desde el despliegue. El UDF `U_sap_id` es de la categoría
Documentos de marketing y está confirmado en las 32 tablas de documentos, OWTR, OWTQ, OIGN, OPDN y
OPOR incluidas (CUFD, 14/09/2026). **Red de
seguridad:** si en alguna tabla o compañía el UDF no existiera, SAP rechaza el POST con
`400 Property 'U_sap_id' ... is invalid`; el middleware lo detecta, vuelve a enviar el documento **sin**
la clave (comportamiento anterior) y registra un error en el log para que se cree el UDF. La clave
nunca impide crear el documento.

Aclaración de nombres: lo que el middleware llama `deposits` es la **Entrada de mercancías de
Inventario** (`InventoryGenEntries`, OIGN), no el Depósito bancario (ODPS), que el middleware no crea.

## Relación con la deduplicación de jobs en curso

La cola también reutiliza un job `ENCOLADO`/`PROCESANDO` cuando llega otra petición con **exactamente
el mismo payload** (mismo endpoint y JSON idéntico), para que un usuario que reintenta a mano mientras
SAP está caído no dispare cadenas paralelas de intentos. Como la clave viaja dentro del payload, esa
deduplicación funciona plenamente cuando el módulo envía su propia `u_sap_id` (dos reenvíos de la
misma operación producen el mismo JSON). Cuando el middleware genera el GUID de respaldo, cada petición
es distinta y la deduplicación de jobs en curso no aplica a ese endpoint; la protección queda a cargo
de la idempotencia contra SAP. Motivo más para que cada módulo envíe su clave.

## Convenciones del identificador

1. **Nombre del campo**: `u_sap_id`, opcional, en el cuerpo JSON de todos los endpoints de creación
   (ya existía con ese nombre en `/direct`). También se acepta la cabecera HTTP `Idempotency-Key`;
   si vienen ambos gana el cuerpo. Se guarda en el UDF `U_sap_id` de la cabecera del documento SAP.
2. **Formato**: `{modulo}-{entidad}-{id}`, minúsculas, letras, dígitos y guion, máximo **60**
   caracteres (el middleware admite además `_ . :`). El prefijo del módulo es lo que evita que el
   `1532` de un módulo choque con el `1532` de otro.
3. **Prefijos registrados** (agregar aquí cualquier módulo nuevo antes de salir a producción):

   | Módulo | Endpoint | Clave |
   |---|---|---|
   | api-recepcion-mercaderia | based-on-document | `reception-transfer-{receptionTransferId}` (ya en producción, se mantiene) |
   | api-transferencias-directas | direct | GUID en mayúsculas, formato "D" (ya en producción, se mantiene) |
   | api-cedi | POST /stock-transfers | `cedi-distribucion-{distribucionId}` |
   | api-cedi | goods-receipts | `cedi-recepcion-{recepcionId}` |
   | api-cedi | purchase-orders | `cedi-odc-{odcId}` |
   | Caja Chica (front web-scnexus) | liquidaciones | `cajachica-liquidacion-{idLiquidacion}` |
   | api-reposiciones | requests | `reposicion-solicitud-{solicitudId}` |
   | módulo de depósitos | deposits | `depositos-{tipo}-{id}` (definir con su equipo) |
   | middleware (respaldo) | todos salvo `/direct` | GUID en mayúsculas cuando el módulo no envía clave |

4. **De dónde sale el `{id}`**: la clave primaria, en la base del módulo, de la operación que
   corresponde **uno a uno** con un documento SAP. Nunca fecha/hora, ni un aleatorio por intento, ni
   datos del usuario.
5. **Estable**: la misma operación produce siempre la misma clave, en cada reintento y después de
   un reinicio. El módulo la guarda junto a la operación **antes** de llamar al middleware.
6. **Una operación que genera varios documentos SAP**: sufijo secuencial persistido en el módulo,
   `{modulo}-{entidad}-{id}-{n}`.
7. **Reutilizar la clave tras un error de negocio (400) está bien**: SAP no creó nada, así que el
   reintento corregido con la misma clave crea el documento. Lo que nunca se hace es usar la clave de
   una operación para otra operación distinta.
8. **Qué valida el middleware**: largo ≤ 60 y caracteres permitidos (`400` antes de llamar a SAP,
   venga la clave en el cuerpo o en la cabecera); que el documento encontrado sea del mismo pedido
   (`409` si no). No valida el prefijo: eso es convención entre equipos.
9. **Qué devuelve**: siempre `201` con `docEntry`, `docNum` y `recovered` (`true` si el documento ya
   existía y no se creó en esa llamada). Los módulos actuales ignoran el campo nuevo sin problema.

## Prerequisitos en SAP

```sql
-- Existencia y tamaño del UDF por tabla. Correr en la compañía de prueba y en producción,
-- y en CompanyDb si difiere de TestCompanyDb.
SELECT TableID, AliasID, TypeID, EditSize
FROM CUFD
WHERE AliasID = 'sap_id' AND TableID IN ('OWTR', 'OWTQ', 'OPDN', 'OIGN', 'OPOR');
```

| Tabla | Documento | Estado |
|---|---|---|
| OWTR | Transferencia de stock | existe, alfanumérico 60 (confirmado 11/09/2026 y en CUFD el 14/09/2026) |
| OWTQ | Solicitud de traslado | existe, alfanumérico 60 (confirmado 14/09/2026 en prueba y producción) |
| OIGN | Inventario › Entrada de mercancías (`/deposits`) | existe, alfanumérico 60 (confirmado 14/09/2026) |
| OPOR | Pedido / orden de compra (`/purchase-orders`) | existe, alfanumérico 60 (confirmado 14/09/2026) |
| OPDN | Compras › Entrada de mercancías (`/goods-receipts`) | existe, alfanumérico 60 (confirmado en CUFD 14/09/2026) |
| OPCH | Factura de proveedor / liquidación (`/liquidaciones`) | existe, alfanumérico 60 (confirmado en CUFD 14/09/2026) |
| ODPS | Depósito bancario | no lo crea el middleware; no se necesita para esto |

Verificado el 14/09/2026 en CUFD: `sap_id` alfanumérico 60 existe en las 32 tablas de documentos de
marketing (ADOC, OCIN, OCPI, OCPV, OCSI, OCSV, ODLN, ODPI, ODPO, ODRF, OIEI, OIGE, OIGN, OINV, OOEI,
OPCH, OPDN, OPOR, OPQT, OPRQ, OPRR, OQUT, ORDN, ORDR, ORIN, ORPC, ORPD, ORRR, OSFC, OSFI, OWTQ, OWTR).
No se requiere ninguna acción en SAP. La red de seguridad del middleware (crear sin clave si SAP
rechazara el UDF) queda como protección ante una compañía nueva donde falte.

## Plan de acción

### Fase 0: middleware (hecho en el árbol local, falta desplegar)

- `based-on-document`: clave opcional (cuerpo, cabecera o GUID), verificación previa, recuperación
  tras fallo, `409` por choque, `recovered` en la respuesta.
- `with-bins`, `POST /stock-transfers` (for-company), `requests`, `goods-receipts`, `deposits`,
  `purchase-orders`, `liquidaciones`: mismo flujo, con GUID de respaldo si el módulo no envía clave.
  En órdenes de compra sustituye la recuperación anterior por CardCode + Comments; en liquidaciones
  se conserva la pista de `AccountCode` inválido.
- Red de seguridad: si SAP rechaza el UDF (no existe en esa tabla/compañía) se crea sin clave y se
  registra un error.
- `by-correlation` y la recuperación de `/direct` buscan en la compañía correcta (TestCompanyDb).
- Validación centralizada de la clave (60 caracteres, formato) y mensaje del job en `ERROR` con el
  último error real.
- Pruebas: 24 de integración nuevas sobre el gateway real con un cliente SAP scriptado.
- Pendiente: commit, PR a `test`, PR a `main`, release. No requiere configuración nueva ni cambios
  en base de datos.

### Fase 1: SAP y middleware, lo que falta

1. `/direct`: agregar la verificación previa, acordado con transferencias directas (hoy su contrato
   dice que cada POST crea un documento nuevo y ellos consultan `by-correlation` por su cuenta).
2. Prueba manual en la compañía de prueba por endpoint: crear con clave, repetir con la misma clave
   y verificar mismo DocEntry con `recovered: true`, repetir con la misma clave y otro pedido y
   verificar `409`.

### Fase 2: módulos consumidores

1. api-recepcion-mercaderia, **bloqueante**: tratar el estado `ERROR` del job como fallo terminal en
   `MiddlewareSapHttpClient.GetJobStatusAsync`. Hoy lo trata como pendiente y la recepción queda
   bloqueada. Opcional: enviar también `u_sap_id` en el cuerpo con la misma clave.
2. api-cedi: enviar `u_sap_id` en `POST /stock-transfers`, `goods-receipts` y `purchase-orders`.
3. api-reposiciones: enviar `u_sap_id = reposicion-solicitud-{id}` en `requests` (el UDF ya existe).
4. Módulo de depósitos (entradas de inventario): identificar y acordar prefijo.
5. Caja Chica (front web-scnexus, `AprobacionesContabilidadHistorialView.vue`): enviar `u_sap_id = cajachica-liquidacion-{idLiquidacion}` en el payload de `/v1/liquidaciones`.
5. api-transferencias-directas: sin cambios.

### Fase 3: operación

1. Remediar las 5 recepciones del 10/09: DocEntry de SAP, reenvío a HITELL con
   `POST /webpos/stock-transfers/{docEntry}/retry`, confirmación manual en recepción.
2. Mostrar `recovered` y `u_sap_id` en la pestaña Cola SAP de Integraciones (ya vienen en el job).
3. Cabecera `X-Client-Module` para registrar el módulo en `api_log` y `job_queue`.

## Riesgos y cómo se mitigan

- **Costo de la búsqueda previa**: un GET más por creación, filtrando por UDF. Medir en la compañía
  de prueba; si tarda, evaluar un índice sobre `U_sap_id`.
- **409 falsos**: el criterio de "mismo pedido" es deliberadamente laxo (documento, no líneas).
- **Módulo sin prefijo**: si dos módulos chocan, el resultado es un `409` explícito, no un duplicado
  ni una transferencia ajena entregada como propia.
- **UDF inexistente**: el middleware crea el documento sin clave y registra un error; se pierde la
  protección en ese endpoint hasta crear el UDF, pero nunca se bloquea la operación.
