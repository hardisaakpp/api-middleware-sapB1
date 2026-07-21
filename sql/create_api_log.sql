-- sc_middleware database
-- Esquema tomado directamente de producción (sc_middleware.dbo.api_log) — ver
-- src/Middleware.Infrastructure/Sql/ApiLogRepository.cs para el INSERT real que lo consume.
--
-- En producción la tabla ya existe: no hace falta correr esto ahí.
-- Para desarrollo/pruebas, donde normalmente no existe todavía, este script la crea si hace falta.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'api_log' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.api_log
    (
        id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_api_log PRIMARY KEY,
        endpoint    NVARCHAR(MAX) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        request     NVARCHAR(MAX) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        response    NVARCHAR(MAX) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        fecha       DATETIME2     NOT NULL,
        usuario     NVARCHAR(MAX) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        status_code INT           NOT NULL,
        duracion_ms BIGINT        NOT NULL
    );
END
