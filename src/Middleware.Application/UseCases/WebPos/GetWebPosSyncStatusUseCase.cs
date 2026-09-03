using Middleware.Application.Abstractions;

namespace Middleware.Application.UseCases.WebPos;

/// <summary>
/// Read-only lookups over pos_sync_status so clients (Nexus) can show the integration state of
/// each document — INTEGRADO (SYNCED), NO CORRESPONDE (SKIPPED_NOT_POS), RECHAZADO (REJECTED) —
/// and drive the re-integration of REJECTED ones via the retry endpoints.
/// </summary>
public sealed class GetWebPosSyncStatusUseCase
{
    private static readonly string[] ValidDocTypes = [PosSyncDocTypes.StockTransfer, PosSyncDocTypes.GoodsReceipt];
    private static readonly string[] ValidStatuses = ["SYNCED", "REJECTED", "SKIPPED_NOT_POS"];

    private readonly IPosSyncStatusRepository _repository;

    public GetWebPosSyncStatusUseCase(IPosSyncStatusRepository repository)
    {
        _repository = repository;
    }

    public Task<PosSyncStatusEntry?> GetAsync(string docType, int docEntry, CancellationToken ct = default)
    {
        var normalized = NormalizeDocType(docType)
            ?? throw new ArgumentException($"docType inválido: '{docType}'. Valores aceptados: ST, GR.");

        return _repository.GetAsync(normalized, docEntry, ct);
    }

    public Task<IReadOnlyList<PosSyncStatusEntry>> ListAsync(string? status, string? docType, int top, CancellationToken ct = default)
    {
        string? normalizedType = null;
        if (!string.IsNullOrWhiteSpace(docType))
        {
            normalizedType = NormalizeDocType(docType)
                ?? throw new ArgumentException($"docType inválido: '{docType}'. Valores aceptados: ST, GR.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalizedStatus = Array.Find(ValidStatuses, s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"status inválido: '{status}'. Valores aceptados: SYNCED, REJECTED, SKIPPED_NOT_POS.");
        }

        var boundedTop = Math.Clamp(top, 1, 500);
        return _repository.ListAsync(normalizedStatus, normalizedType, boundedTop, ct);
    }

    private static string? NormalizeDocType(string docType) =>
        Array.Find(ValidDocTypes, t => t.Equals(docType.Trim(), StringComparison.OrdinalIgnoreCase));
}
