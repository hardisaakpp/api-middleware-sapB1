using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Sql;

internal sealed class PosSyncStatusRepository : IPosSyncStatusRepository
{
    private readonly SqlSettings _settings;

    public PosSyncStatusRepository(IOptions<SqlSettings> settings) => _settings = settings.Value;

    public async Task UpsertAsync(string docType, int docEntry, string status, string? reason, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            MERGE dbo.pos_sync_status AS target
            USING (SELECT @docType AS doc_type, @docEntry AS doc_entry) AS source
            ON target.doc_type = source.doc_type AND target.doc_entry = source.doc_entry
            WHEN MATCHED THEN
                UPDATE SET status = @status, reason = @reason, attempts = target.attempts + 1, updated_at = SYSDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (doc_type, doc_entry, status, reason, attempts, created_at)
                VALUES (@docType, @docEntry, @status, @reason, 1, SYSDATETIME());
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@docType", docType);
        cmd.Parameters.AddWithValue("@docEntry", docEntry);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<PosSyncStatusEntry?> GetAsync(string docType, int docEntry, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            SELECT doc_type, doc_entry, status, reason, attempts, created_at, updated_at
            FROM dbo.pos_sync_status
            WHERE doc_type = @docType AND doc_entry = @docEntry;
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@docType", docType);
        cmd.Parameters.AddWithValue("@docEntry", docEntry);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapEntry(reader) : null;
    }

    public async Task<IReadOnlyList<PosSyncStatusEntry>> ListAsync(string? status, string? docType, int? docEntry, int top, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            SELECT TOP (@top) doc_type, doc_entry, status, reason, attempts, created_at, updated_at
            FROM dbo.pos_sync_status
            WHERE (@status IS NULL OR status = @status)
              AND (@docType IS NULL OR doc_type = @docType)
              AND (@docEntry IS NULL OR doc_entry = @docEntry)
            ORDER BY COALESCE(updated_at, created_at) DESC;
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@top", top);
        cmd.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@docType", (object?)docType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@docEntry", (object?)docEntry ?? DBNull.Value);

        var result = new List<PosSyncStatusEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(MapEntry(reader));
        }

        return result;
    }

    private static PosSyncStatusEntry MapEntry(SqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetInt32(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetInt32(4),
        reader.GetDateTime(5),
        reader.IsDBNull(6) ? null : reader.GetDateTime(6));
}
