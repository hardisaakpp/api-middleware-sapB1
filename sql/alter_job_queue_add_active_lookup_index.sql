-- sc_middleware database
-- Soporta JobQueueRepository.FindActiveIdAsync: busca un job ENCOLADO/PROCESANDO para el mismo
-- endpoint/request_type antes de disparar otro intento contra SAP (dedup de reintentos manuales
-- del usuario mientras SAP esta lento/caido). request_payload es NVARCHAR(MAX) y no puede ir en
-- la clave del indice, asi que el filtro por payload queda como predicado residual sobre el
-- set ya reducido por status+request_type+endpoint. Idempotente.

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_job_queue_active_lookup' AND object_id = OBJECT_ID('dbo.job_queue'))
BEGIN
    CREATE INDEX IX_job_queue_active_lookup ON dbo.job_queue (status, request_type, endpoint) INCLUDE (created_at);
END
