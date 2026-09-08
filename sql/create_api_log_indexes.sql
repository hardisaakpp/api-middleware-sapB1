-- sc_middleware database
-- Índices para la consulta de la bitácora desde Nexus (GET /api/v1/api-logs): el listado ordena
-- por fecha descendente y filtra por status_code, y la tabla crece con cada petición al middleware.
-- Sin estos índices cada página recorre la tabla completa. Idempotente.
-- Ejecutar manualmente contra el ambiente correspondiente antes de desplegar la pantalla de integraciones.

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_api_log_fecha' AND object_id = OBJECT_ID('dbo.api_log'))
BEGIN
    CREATE INDEX IX_api_log_fecha ON dbo.api_log (fecha DESC, id DESC) INCLUDE (status_code, duracion_ms, usuario);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_api_log_status_code_fecha' AND object_id = OBJECT_ID('dbo.api_log'))
BEGIN
    CREATE INDEX IX_api_log_status_code_fecha ON dbo.api_log (status_code, fecha DESC);
END

-- job_queue: el listado de soporte ordena por created_at.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_job_queue_created_at' AND object_id = OBJECT_ID('dbo.job_queue'))
BEGIN
    CREATE INDEX IX_job_queue_created_at ON dbo.job_queue (created_at DESC);
END
