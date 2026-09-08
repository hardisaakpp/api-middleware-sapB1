using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.Abstractions;

/// <summary>
/// Trazabilidad de la integración de stock a WEBPOS/HITELL por documento SAP. La clave es
/// (docType, docEntry) porque los DocEntry de StockTransfers (OWTR) y de PurchaseDeliveryNotes
/// (OPDN) son secuencias independientes en SAP y pueden colisionar entre sí. <c>docNum</c> es el
/// número visible del documento en SAP, que es por el que busca el usuario de soporte, y
/// <c>warehouses</c> son los almacenes del documento con su negocio WEBPOS: es donde soporte tiene
/// que crear el producto cuando HITELL rechaza por "producto no existe".
/// </summary>
public interface IPosSyncStatusRepository
{
    Task UpsertAsync(
        string docType,
        int docEntry,
        int? docNum,
        string status,
        string? reason,
        IReadOnlyCollection<PosSyncWarehouse> warehouses,
        CancellationToken ct = default);

    /// <summary>Completa el DocNum de una fila registrada por una versión anterior que no lo guardaba.</summary>
    Task UpdateDocNumAsync(string docType, int docEntry, int docNum, CancellationToken ct = default);

    /// <summary>Completa los almacenes de una fila que no los tenía (solo si siguen vacíos).</summary>
    Task UpdateWarehousesAsync(string docType, int docEntry, IReadOnlyCollection<PosSyncWarehouse> warehouses, CancellationToken ct = default);

    Task<PosSyncStatusEntry?> GetAsync(string docType, int docEntry, CancellationToken ct = default);

    /// <summary>
    /// Listado paginado, más reciente primero (por última actualización), filtrando opcionalmente
    /// por estado, tipo de documento, DocEntry, DocNum y rango de fechas — pensado para la
    /// pestaña HITELL del módulo Integraciones de Nexus.
    /// </summary>
    Task<PagedResult<PosSyncStatusEntry>> ListAsync(PosSyncStatusFilter filter, CancellationToken ct = default);
}

/// <summary>Tipos de documento rastreados en pos_sync_status.</summary>
public static class PosSyncDocTypes
{
    /// <summary>StockTransfer (OWTR).</summary>
    public const string StockTransfer = "ST";

    /// <summary>Entrada de mercadería / PurchaseDeliveryNote (OPDN).</summary>
    public const string GoodsReceipt = "GR";
}

/// <summary>Almacén involucrado en la notificación y su negocio WEBPOS (null si el almacén no está habilitado para WEBPOS).</summary>
public sealed record PosSyncWarehouse(string Code, string? CodBusiness);

/// <summary>Filtros del listado de pos_sync_status. Las fechas acotan la última actualización (o la creación si nunca se actualizó).</summary>
public sealed record PosSyncStatusFilter(
    string? Status,
    string? DocType,
    int? DocEntry,
    int? DocNum,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize);

public sealed record PosSyncStatusEntry(
    string DocType,
    int DocEntry,
    int? DocNum,
    string Status,
    string? Reason,
    int Attempts,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<PosSyncWarehouse> Warehouses);
