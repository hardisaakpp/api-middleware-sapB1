-- sc_middleware database
-- Tabla de trazabilidad/idempotencia para la notificación de transferencias de stock a WEBPOS
-- (ver docs/api.md, sección "WEBPOS"). Permite distinguir un rechazo de negocio (REJECTED,
-- requiere corrección manual en WEBPOS + reintento explícito) de un almacén fuera de alcance
-- de WEBPOS (SKIPPED_NOT_POS, no es un error).
-- Ejecutar manualmente contra el ambiente correspondiente antes de desplegar esta funcionalidad.

CREATE TABLE dbo.pos_sync_status
(
    doc_entry  INT           NOT NULL CONSTRAINT PK_pos_sync_status PRIMARY KEY,
    status     NVARCHAR(20)  NOT NULL, -- SYNCED | REJECTED | SKIPPED_NOT_POS
    reason     NVARCHAR(MAX) NULL,
    attempts   INT           NOT NULL CONSTRAINT DF_pos_sync_status_attempts DEFAULT (0),
    created_at DATETIME2     NOT NULL CONSTRAINT DF_pos_sync_status_created_at DEFAULT (SYSDATETIME()),
    updated_at DATETIME2     NULL
);

CREATE INDEX IX_pos_sync_status_status ON dbo.pos_sync_status (status);
