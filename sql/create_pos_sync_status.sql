-- sc_middleware database
-- Tabla de trazabilidad/idempotencia para la notificación de movimientos de stock a WEBPOS/HITELL
-- (ver docs/api.md, sección "WEBPOS"). Permite distinguir un rechazo de negocio (REJECTED,
-- requiere corrección manual en WEBPOS + reintento explícito) de un almacén fuera de alcance
-- de WEBPOS (SKIPPED_NOT_POS, no es un error).
-- doc_type: 'ST' = StockTransfer (OWTR), 'GR' = entrada de mercadería / PurchaseDeliveryNote (OPDN).
-- La clave es compuesta porque los DocEntry de OWTR y OPDN son secuencias independientes en SAP.
-- doc_num: número visible del documento en SAP, por el que busca soporte desde Nexus.
-- Ejecutar manualmente contra el ambiente correspondiente antes de desplegar esta funcionalidad.
-- Idempotente. Si la tabla ya existe sin doc_num, correr alter_pos_sync_status_add_doc_num.sql.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'pos_sync_status' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.pos_sync_status
    (
        doc_type   NVARCHAR(2)   NOT NULL, -- ST | GR
        doc_entry  INT           NOT NULL,
        doc_num    INT           NULL,
        status     NVARCHAR(20)  NOT NULL, -- SYNCED | REJECTED | SKIPPED_NOT_POS
        reason     NVARCHAR(MAX) NULL,
        attempts   INT           NOT NULL CONSTRAINT DF_pos_sync_status_attempts DEFAULT (0),
        created_at DATETIME2     NOT NULL CONSTRAINT DF_pos_sync_status_created_at DEFAULT (SYSDATETIME()),
        updated_at DATETIME2     NULL,
        CONSTRAINT PK_pos_sync_status PRIMARY KEY (doc_type, doc_entry)
    );

    CREATE INDEX IX_pos_sync_status_status ON dbo.pos_sync_status (status);
    CREATE INDEX IX_pos_sync_status_doc_num ON dbo.pos_sync_status (doc_num);
END
