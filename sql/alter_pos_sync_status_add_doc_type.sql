-- sc_middleware database
-- Migración para ambientes donde dbo.pos_sync_status ya existe con PK solo por doc_entry
-- (versión previa, únicamente StockTransfers): agrega doc_type y re-arma la PK compuesta.
-- Las filas existentes se marcan como 'ST' (el único tipo que existía).
-- Idempotente: no hace nada si doc_type ya existe.

IF COL_LENGTH('dbo.pos_sync_status', 'doc_type') IS NULL
BEGIN
    ALTER TABLE dbo.pos_sync_status ADD doc_type NVARCHAR(2) NOT NULL
        CONSTRAINT DF_pos_sync_status_doc_type DEFAULT ('ST');

    ALTER TABLE dbo.pos_sync_status DROP CONSTRAINT PK_pos_sync_status;
    ALTER TABLE dbo.pos_sync_status ADD CONSTRAINT PK_pos_sync_status PRIMARY KEY (doc_type, doc_entry);

    -- El default solo existía para poblar las filas viejas; las nuevas siempre mandan doc_type.
    ALTER TABLE dbo.pos_sync_status DROP CONSTRAINT DF_pos_sync_status_doc_type;
END
