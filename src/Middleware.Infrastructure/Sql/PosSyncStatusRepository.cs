using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Sql;

internal sealed class PosSyncStatusRepository : IPosSyncStatusRepository
{
    private const string Columns = "doc_type, doc_entry, doc_num, status, reason, attempts, created_at, updated_at";

    private readonly SqlSettings _settings;

    public PosSyncStatusRepository(IOptions<SqlSettings> settings) => _settings = settings.Value;

    public async Task UpsertAsync(string docType, int docEntry, int? docNum, string status, string? reason, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        // doc_num se conserva si el reintento no lo trae (COALESCE): un retry siempre parte del mismo documento SAP.
        const string sql = """
            MERGE dbo.pos_sync_status AS target
            USING (SELECT @docType AS doc_type, @docEntry AS doc_entry) AS source
            ON target.doc_type = source.doc_type AND target.doc_entry = source.doc_entry
            WHEN MATCHED THEN
                UPDATE SET status = @status, reason = @reason, doc_num = COALESCE(@docNum, target.doc_num),
                           attempts = target.attempts + 1, updated_at = SYSDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (doc_type, doc_entry, doc_num, status, reason, attempts, created_at)
                VALUES (@docType, @docEntry, @docNum, @status, @reason, 1, SYSDATETIME());
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@docType", docType);
        cmd.Parameters.AddWithValue("@docEntry", docEntry);
        cmd.Parameters.AddWithValue("@docNum", (object?)docNum ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<PosSyncStatusEntry?> GetAsync(string docType, int docEntry, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string sql = $"""
            SELECT {Columns}
            FROM dbo.pos_sync_status
            WHERE doc_type = @docType AND doc_entry = @docEntry;
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@docType", docType);
        cmd.Parameters.AddWithValue("@docEntry", docEntry);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapEntry(reader) : null;
    }

    public async Task<PagedResult<PosSyncStatusEntry>> ListAsync(PosSyncStatusFilter filter, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        const string where = """
            WHERE (@status IS NULL OR status = @status)
              AND (@docType IS NULL OR doc_type = @docType)
              AND (@docEntry IS NULL OR doc_entry = @docEntry)
              AND (@docNum IS NULL OR doc_num = @docNum)
              AND (@from IS NULL OR COALESCE(updated_at, created_at) >= @from)
              AND (@to IS NULL OR COALESCE(updated_at, created_at) <= @to)
            """;

        const string sql = $"""
            SELECT COUNT(1) FROM dbo.pos_sync_status
            {where};

            SELECT {Columns}
            FROM dbo.pos_sync_status
            {where}
            ORDER BY COALESCE(updated_at, created_at) DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
            """;

        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 15 };
        cmd.Parameters.AddWithValue("@status", (object?)filter.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@docType", (object?)filter.DocType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@docEntry", (object?)filter.DocEntry ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@docNum", (object?)filter.DocNum ?? DBNull.Value);
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

        var items = new List<PosSyncStatusEntry>();
        if (await reader.NextResultAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                items.Add(MapEntry(reader));
            }
        }

        return new PagedResult<PosSyncStatusEntry>(items, total, filter.Page, filter.PageSize);
    }

    private static PosSyncStatusEntry MapEntry(SqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetInt32(1),
        reader.IsDBNull(2) ? null : reader.GetInt32(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetInt32(5),
        reader.GetDateTime(6),
        reader.IsDBNull(7) ? null : reader.GetDateTime(7));
}
