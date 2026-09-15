-- sc_middleware database
-- Migración para ambientes donde dbo.pos_sync_status ya existe sin doc_num (versión anterior a la
-- pestaña HITELL del módulo Integraciones de Nexus, que busca por número de documento SAP).
-- Las filas existentes quedan con doc_num NULL; el middleware lo completa en el siguiente
-- reintento de cada documento. Idempotente.

IF COL_LENGTH('dbo.pos_sync_status', 'doc_num') IS NULL
BEGIN
    ALTER TABLE dbo.pos_sync_status ADD doc_num INT NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_pos_sync_status_doc_num' AND object_id = OBJECT_ID('dbo.pos_sync_status'))
BEGIN
    CREATE INDEX IX_pos_sync_status_doc_num ON dbo.pos_sync_status (doc_num);
END
