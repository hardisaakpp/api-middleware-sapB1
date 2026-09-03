namespace Middleware.Application.Abstractions;

/// <summary>
/// Trazabilidad de la integración de stock a WEBPOS/HITELL por documento SAP. La clave es
/// (docType, docEntry) porque los DocEntry de StockTransfers (OWTR) y de PurchaseDeliveryNotes
/// (OPDN) son secuencias independientes en SAP y pueden colisionar entre sí.
/// </summary>
public interface IPosSyncStatusRepository
{
    Task UpsertAsync(string docType, int docEntry, string status, string? reason, CancellationToken ct = default);

    Task<PosSyncStatusEntry?> GetAsync(string docType, int docEntry, CancellationToken ct = default);

    /// <summary>
    /// Lista entradas filtrando opcionalmente por estado y/o tipo de documento, más reciente
    /// primero — pensado para que el cliente (Nexus) muestre las pendientes de reintegración
    /// (REJECTED) y dispare el retry correspondiente.
    /// </summary>
    Task<IReadOnlyList<PosSyncStatusEntry>> ListAsync(string? status, string? docType, int top, CancellationToken ct = default);
}

/// <summary>Tipos de documento rastreados en pos_sync_status.</summary>
public static class PosSyncDocTypes
{
    /// <summary>StockTransfer (OWTR).</summary>
    public const string StockTransfer = "ST";

    /// <summary>Entrada de mercadería / PurchaseDeliveryNote (OPDN).</summary>
    public const string GoodsReceipt = "GR";
}

public sealed record PosSyncStatusEntry(
    string DocType,
    int DocEntry,
    string Status,
    string? Reason,
    int Attempts,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
