-- sc_middleware database
-- Tabla nueva para la cola durable de peticiones a SAP (ver docs/api.md, sección "Colas y procesamiento asíncrono").
-- Ejecutar manualmente contra el ambiente correspondiente antes de desplegar esta funcionalidad.

CREATE TABLE dbo.job_queue
(
    id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_job_queue PRIMARY KEY,
    endpoint         NVARCHAR(200)    NOT NULL,
    request_type     NVARCHAR(100)    NOT NULL,
    request_payload  NVARCHAR(MAX)    NOT NULL,
    status           NVARCHAR(20)     NOT NULL, -- ENCOLADO | PROCESANDO | COMPLETADO | ERROR
    http_status_code INT              NULL,
    result_payload   NVARCHAR(MAX)    NULL,
    error_message    NVARCHAR(MAX)    NULL,
    attempts         INT              NOT NULL CONSTRAINT DF_job_queue_attempts DEFAULT (0),
    next_retry_at    DATETIME2        NULL,
    created_at       DATETIME2        NOT NULL CONSTRAINT DF_job_queue_created_at DEFAULT (SYSDATETIME()),
    updated_at       DATETIME2        NULL
);

CREATE INDEX IX_job_queue_status ON dbo.job_queue (status);
