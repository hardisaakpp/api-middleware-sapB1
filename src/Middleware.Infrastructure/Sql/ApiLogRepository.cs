using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Sql;

internal sealed class ApiLogRepository : IApiLogRepository
{
    private readonly SqlSettings _settings;

    public ApiLogRepository(IOptions<SqlSettings> settings) => _settings = settings.Value;

    public async Task InsertAsync(ApiLogEntry entry, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            INSERT INTO dbo.api_log (endpoint, request, response, fecha, usuario, status_code, duracion_ms)
            VALUES (@endpoint, @request, @response, @fecha, @usuario, @statusCode, @duracionMs)
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 5 };
        cmd.Parameters.AddWithValue("@endpoint",   entry.Endpoint);
        cmd.Parameters.AddWithValue("@request",    entry.Request  ?? string.Empty);
        cmd.Parameters.AddWithValue("@response",   entry.Response ?? string.Empty);

        cmd.Parameters.AddWithValue("@fecha",      DateTime.UtcNow.AddHours(-5));
        cmd.Parameters.AddWithValue("@usuario",    entry.Usuario);
        cmd.Parameters.AddWithValue("@statusCode", entry.StatusCode);
        cmd.Parameters.AddWithValue("@duracionMs", entry.DurationMs);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<PagedResult<ApiLogRecord>> ListAsync(ApiLogFilter filter, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string where = """
            WHERE (@endpoint IS NULL OR endpoint LIKE '%' + @endpoint + '%')
              AND (@usuario IS NULL OR usuario LIKE '%' + @usuario + '%')
              AND (@statusCode IS NULL OR status_code = @statusCode)
              AND (@onlyErrors = 0 OR status_code >= 400)
              AND (@from IS NULL OR fecha >= @from)
              AND (@to IS NULL OR fecha <= @to)
            """;

        // request/response no se cargan en el listado: pueden pesar hasta 2 KB por fila y solo
        // interesan en el detalle (GetByIdAsync).
        const string sql = $"""
            SELECT COUNT(1) FROM dbo.api_log
            {where};

            SELECT id, endpoint, fecha, usuario, status_code, duracion_ms
            FROM dbo.api_log
            {where}
            ORDER BY fecha DESC, id DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 20 };
        cmd.Parameters.AddWithValue("@endpoint", (object?)filter.Endpoint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@usuario", (object?)filter.Usuario ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@statusCode", (object?)filter.StatusCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@onlyErrors", filter.OnlyErrors ? 1 : 0);
        cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = (object?)filter.From ?? DBNull.Value;
        cmd.Parameters.Add("@to", SqlDbType.DateTime2).Value = (object?)filter.To ?? DBNull.Value;
        cmd.Parameters.AddWithValue("@offset", (filter.Page - 1) * filter.PageSize);
        cmd.Parameters.AddWithValue("@pageSize", filter.PageSize);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var total = 0;
        if (await reader.ReadAsync(ct))
        {
            total = reader.GetInt32(0);
        }

        var items = new List<ApiLogRecord>();
        if (await reader.NextResultAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                items.Add(new ApiLogRecord(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    null,
                    null,
                    reader.GetDateTime(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt64(5)));
            }
        }

        return new PagedResult<ApiLogRecord>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<ApiLogRecord?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            SELECT id, endpoint, request, response, fecha, usuario, status_code, duracion_ms
            FROM dbo.api_log
            WHERE id = @id
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new ApiLogRecord(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetDateTime(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt64(7));
    }
}
