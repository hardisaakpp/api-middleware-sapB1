using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
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
        // Ecuador no observa horario de verano, por lo que el offset -5 es fijo;
        // el contenedor corre en UTC, así que DateTime.Now guardaría la hora adelantada 5 horas.
        cmd.Parameters.AddWithValue("@fecha",      DateTime.UtcNow.AddHours(-5));
        cmd.Parameters.AddWithValue("@usuario",    entry.Usuario);
        cmd.Parameters.AddWithValue("@statusCode", entry.StatusCode);
        cmd.Parameters.AddWithValue("@duracionMs", entry.DurationMs);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
