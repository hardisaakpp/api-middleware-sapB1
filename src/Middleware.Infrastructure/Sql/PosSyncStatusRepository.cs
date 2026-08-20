using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Sql;

internal sealed class PosSyncStatusRepository : IPosSyncStatusRepository
{
    private readonly SqlSettings _settings;

    public PosSyncStatusRepository(IOptions<SqlSettings> settings) => _settings = settings.Value;

    public async Task UpsertAsync(int docEntry, string status, string? reason, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            MERGE dbo.pos_sync_status AS target
            USING (SELECT @docEntry AS doc_entry) AS source
            ON target.doc_entry = source.doc_entry
            WHEN MATCHED THEN
                UPDATE SET status = @status, reason = @reason, attempts = target.attempts + 1, updated_at = SYSDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (doc_entry, status, reason, attempts, created_at)
                VALUES (@docEntry, @status, @reason, 1, SYSDATETIME());
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@docEntry", docEntry);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
