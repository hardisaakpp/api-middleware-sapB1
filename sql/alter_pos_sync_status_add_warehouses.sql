-- sc_middleware database
-- Agrega a dbo.pos_sync_status la columna warehouses (JSON: [{"code":"LP-CON","codBusiness":"9"}, ...])
-- con los almacenes del documento y su negocio WEBPOS: es lo que el usuario de soporte necesita para
-- saber en qué tienda crear el producto cuando HITELL rechaza por "producto no existe".
-- Las filas existentes quedan en NULL; el middleware la completa en el siguiente reintento.
-- Idempotente. Ejecutar en cada ambiente antes de desplegar esta versión.

IF COL_LENGTH('dbo.pos_sync_status', 'warehouses') IS NULL
BEGIN
    ALTER TABLE dbo.pos_sync_status ADD warehouses NVARCHAR(MAX) NULL;
END
