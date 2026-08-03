using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Jobs;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Sql;

internal sealed class JobQueueRepository : IJobQueueRepository
{
    private const string StatusParam = "@status";

    private readonly SqlSettings _settings;

    public JobQueueRepository(IOptions<SqlSettings> settings) => _settings = settings.Value;

    public async Task InsertQueuedAsync(Guid jobId, string endpoint, string requestType, string requestPayloadJson, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            INSERT INTO dbo.job_queue (id, endpoint, request_type, request_payload, status, attempts, created_at)
            VALUES (@id, @endpoint, @requestType, @requestPayload, @status, 0, SYSDATETIME())
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@id", jobId);
        cmd.Parameters.AddWithValue("@endpoint", endpoint);
        cmd.Parameters.AddWithValue("@requestType", requestType);
        cmd.Parameters.AddWithValue("@requestPayload", requestPayloadJson);
        cmd.Parameters.AddWithValue(StatusParam, JobStatuses.Encolado);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> MarkProcessingAsync(Guid jobId, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            UPDATE dbo.job_queue
            SET status = @processing, updated_at = SYSDATETIME()
            WHERE id = @id AND status IN (@encolado, @processing)
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@id", jobId);
        cmd.Parameters.AddWithValue("@processing", JobStatuses.Procesando);
        cmd.Parameters.AddWithValue("@encolado", JobStatuses.Encolado);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task MarkCompletedAsync(Guid jobId, int httpStatusCode, string responsePayloadJson, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            UPDATE dbo.job_queue
            SET status = @status, http_status_code = @httpStatusCode, result_payload = @resultPayload, updated_at = SYSDATETIME()
            WHERE id = @id
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@id", jobId);
        cmd.Parameters.AddWithValue(StatusParam, JobStatuses.Completado);
        cmd.Parameters.AddWithValue("@httpStatusCode", httpStatusCode);
        cmd.Parameters.AddWithValue("@resultPayload", responsePayloadJson);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkErrorAsync(Guid jobId, string errorMessage, int httpStatusCode, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            UPDATE dbo.job_queue
            SET status = @status, error_message = @errorMessage, http_status_code = @httpStatusCode, updated_at = SYSDATETIME()
            WHERE id = @id
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@id", jobId);
        cmd.Parameters.AddWithValue(StatusParam, JobStatuses.Error);
        cmd.Parameters.AddWithValue("@errorMessage", errorMessage);
        cmd.Parameters.AddWithValue("@httpStatusCode", httpStatusCode);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ScheduleRetryAsync(Guid jobId, int attempts, DateTime nextRetryAt, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            UPDATE dbo.job_queue
            SET status = @status, attempts = @attempts, next_retry_at = @nextRetryAt, updated_at = SYSDATETIME()
            WHERE id = @id
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@id", jobId);
        cmd.Parameters.AddWithValue(StatusParam, JobStatuses.Encolado);
        cmd.Parameters.AddWithValue("@attempts", attempts);
        cmd.Parameters.AddWithValue("@nextRetryAt", nextRetryAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<JobRecord?> GetByIdAsync(Guid jobId, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            SELECT id, endpoint, status, http_status_code, result_payload, error_message, attempts, created_at, updated_at
            FROM dbo.job_queue
            WHERE id = @id
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@id", jobId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new JobRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            await reader.IsDBNullAsync(3, ct) ? null : reader.GetInt32(3),
            await reader.IsDBNullAsync(4, ct) ? null : reader.GetString(4),
            await reader.IsDBNullAsync(5, ct) ? null : reader.GetString(5),
            reader.GetInt32(6),
            reader.GetDateTime(7),
            await reader.IsDBNullAsync(8, ct) ? null : reader.GetDateTime(8)
        );
    }

    public async Task<IReadOnlyList<OrphanedJob>> GetOrphanedAsync(CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            SELECT id, request_type, request_payload, attempts
            FROM dbo.job_queue
            WHERE status IN (@encolado, @processing)
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@encolado", JobStatuses.Encolado);
        cmd.Parameters.AddWithValue("@processing", JobStatuses.Procesando);

        var results = new List<OrphanedJob>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new OrphanedJob(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3)));
        }

        return results;
    }
}
