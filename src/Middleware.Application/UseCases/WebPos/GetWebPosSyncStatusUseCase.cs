using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.WebPos;

namespace Middleware.Application.UseCases.WebPos;

/// <summary>
/// Read-only lookups over pos_sync_status so clients (Nexus) can show the integration state of
/// each document — INTEGRADO (SYNCED), NO_CORRESPONDE (SKIPPED_NOT_POS), RECHAZADO (REJECTED) —
/// and drive the re-integration of REJECTED ones via the retry endpoints. Every entry is returned
/// already interpreted (see <see cref="WebPosSyncStatusResponse"/>) so the UI never has to parse
/// HITELL's raw messages.
/// </summary>
public sealed class GetWebPosSyncStatusUseCase
{
    public const int MaxPageSize = 200;

    /// <summary>
    /// El tipo de rechazo se deduce del texto de HITELL, no está en una columna: para filtrar por él
    /// se traen los rechazados del rango (acotado) y se pagina en memoria.
    /// </summary>
    private const int MaxRejectedScanned = 2000;

    private static readonly string[] ValidDocTypes = [PosSyncDocTypes.StockTransfer, PosSyncDocTypes.GoodsReceipt];
    private static readonly string[] ValidStatuses = ["SYNCED", "REJECTED", "SKIPPED_NOT_POS", "ERROR"];
    private static readonly string[] ValidRejectionTypes = [WebPosRejectionTypes.ProductNotFound, WebPosRejectionTypes.Other];

    private readonly IPosSyncStatusRepository _repository;
    private readonly IApiLogRepository _apiLog;
    private readonly ISapGateway _sapGateway;
    private readonly ILogger<GetWebPosSyncStatusUseCase> _logger;

    public GetWebPosSyncStatusUseCase(
        IPosSyncStatusRepository repository,
        IApiLogRepository apiLog,
        ISapGateway sapGateway,
        ILogger<GetWebPosSyncStatusUseCase> logger)
    {
        _repository = repository;
        _apiLog = apiLog;
        _sapGateway = sapGateway;
        _logger = logger;
    }

    public async Task<WebPosSyncStatusResponse?> GetAsync(string docType, int docEntry, CancellationToken ct = default)
    {
        var normalized = NormalizeDocType(docType)
            ?? throw new ArgumentException($"docType inválido: '{docType}'. Valores aceptados: ST, GR.");

        var entry = await _repository.GetAsync(normalized, docEntry, ct);
        return entry is null ? null : ToResponse(await EnsureDocNumAsync(entry, ct));
    }

    public async Task<PagedResult<WebPosSyncStatusResponse>> ListAsync(
        WebPosSyncStatusQuery query, CancellationToken ct = default)
    {
        string? normalizedType = null;
        if (!string.IsNullOrWhiteSpace(query.DocType))
        {
            normalizedType = NormalizeDocType(query.DocType)
                ?? throw new ArgumentException($"docType inválido: '{query.DocType}'. Valores aceptados: ST, GR.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            normalizedStatus = Array.Find(ValidStatuses, s => s.Equals(query.Status.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"status inválido: '{query.Status}'. Valores aceptados: SYNCED, REJECTED, SKIPPED_NOT_POS, ERROR.");
        }

        string? normalizedRejectionType = null;
        if (!string.IsNullOrWhiteSpace(query.RejectionType))
        {
            normalizedRejectionType = Array.Find(ValidRejectionTypes, t => t.Equals(query.RejectionType.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"rejectionType inválido: '{query.RejectionType}'. Valores aceptados: PRODUCTO_NO_EXISTE, OTRO.");
            // A rejection type only makes sense over rejected rows.
            normalizedStatus = "REJECTED";
        }

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        if (normalizedRejectionType is null)
        {
            var result = await _repository.ListAsync(
                new PosSyncStatusFilter(normalizedStatus, normalizedType, query.DocEntry, query.DocNum, query.From, query.To, page, pageSize), ct);
            var items = await EnsureDocNumsAsync(result.Items, ct);
            return new PagedResult<WebPosSyncStatusResponse>(items.Select(ToResponse).ToList(), result.Total, page, pageSize);
        }

        var rejected = await _repository.ListAsync(
            new PosSyncStatusFilter(normalizedStatus, normalizedType, query.DocEntry, query.DocNum, query.From, query.To, 1, MaxRejectedScanned), ct);
        var matching = rejected.Items
            .Select(ToResponse)
            .Where(r => r.Rejection?.Type == normalizedRejectionType)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        var matchingTotal = rejected.Items.Count(e => WebPosRejectionClassifier.ClassifyReason(e.Reason) == normalizedRejectionType);

        // Solo la página visible se completa contra SAP.
        var completed = await EnsureDocNumsAsync(
            rejected.Items.Where(e => matching.Exists(m => m.DocType == e.DocType && m.DocEntry == e.DocEntry)).ToList(), ct);

        return new PagedResult<WebPosSyncStatusResponse>(completed.Select(ToResponse).ToList(), matchingTotal, page, pageSize);
    }

    /// <summary>
    /// Las filas registradas por versiones anteriores del middleware no tienen DocNum ni almacenes,
    /// que es lo que le sirve al usuario de soporte. Se completan una sola vez (quedan persistidas):
    /// primero desde la bitácora (la petición que creó el documento trae el DocNum que devolvió SAP y
    /// los almacenes enviados, sin depender de SAP) y, si allí no está, leyendo el documento en SAP.
    /// Si nada responde, la fila se devuelve tal cual.
    /// </summary>
    private async Task<IReadOnlyList<PosSyncStatusEntry>> EnsureDocNumsAsync(IReadOnlyList<PosSyncStatusEntry> entries, CancellationToken ct)
    {
        if (entries.All(e => e.DocNum is not null && e.Warehouses.Count > 0))
        {
            return entries;
        }

        var completed = new List<PosSyncStatusEntry>(entries.Count);
        foreach (var entry in entries)
        {
            completed.Add(await EnsureDocNumAsync(entry, ct));
        }

        return completed;
    }

    private async Task<PosSyncStatusEntry> EnsureDocNumAsync(PosSyncStatusEntry entry, CancellationToken ct)
    {
        if (entry.DocNum is not null && entry.Warehouses.Count > 0)
        {
            return entry;
        }

        var current = entry;
        try
        {
            // 1) Bitácora: la petición que creó el documento (sin depender de SAP).
            var trace = await _apiLog.FindDocumentTraceAsync(entry.DocType, entry.DocEntry, ct);
            var docNum = trace?.DocNum;
            IReadOnlyList<string> codes = trace?.WarehouseCodes ?? [];

            // 2) SAP: el documento mismo, si a la bitácora le falta el número o los almacenes
            //    (p. ej. peticiones cuyo cuerpo no quedó registrado).
            if ((current.DocNum is null && docNum is null) || (current.Warehouses.Count == 0 && codes.Count == 0))
            {
                var fromSap = await ReadDocumentFromSapAsync(current, ct);
                docNum ??= fromSap?.DocNum;
                if (codes.Count == 0 && fromSap is not null)
                {
                    codes = fromSap.WarehouseCodes;
                }
            }

            if (current.DocNum is null && docNum is not null)
            {
                await _repository.UpdateDocNumAsync(current.DocType, current.DocEntry, docNum.Value, ct);
                current = current with { DocNum = docNum };
            }

            if (current.Warehouses.Count == 0 && codes.Count > 0)
            {
                var warehouses = await ResolveWarehousesAsync(current.DocType, codes, ct);
                if (warehouses is not null)
                {
                    await _repository.UpdateWarehousesAsync(current.DocType, current.DocEntry, warehouses, ct);
                    current = current with { Warehouses = warehouses };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo completar DocNum/almacenes de {DocType} DocEntry={DocEntry}.", entry.DocType, entry.DocEntry);
        }

        return current;
    }

    /// <summary>DocNum y almacenes leídos del documento en SAP; null si SAP no responde o el documento no existe.</summary>
    private async Task<ApiLogDocumentTrace?> ReadDocumentFromSapAsync(PosSyncStatusEntry entry, CancellationToken ct)
    {
        try
        {
            if (entry.DocType == PosSyncDocTypes.GoodsReceipt)
            {
                var receipt = await _sapGateway.GetGoodsReceiptAsync(entry.DocEntry, ct);
                return receipt is null
                    ? null
                    : new ApiLogDocumentTrace(receipt.DocNum, receipt.Lines.Select(l => l.WarehouseCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            }

            var transfer = await _sapGateway.GetStockTransferForTestCompanyAsync(entry.DocEntry, ct);
            return transfer is null
                ? null
                : new ApiLogDocumentTrace(transfer.DocNum, new[] { transfer.FromWarehouse, transfer.ToWarehouse }.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SAP no respondió al leer {DocType} DocEntry={DocEntry}.", entry.DocType, entry.DocEntry);
            return null;
        }
    }

    /// <summary>
    /// El negocio WEBPOS de cada almacén solo lo sabe SAP (UDF U_WEBPOS / U_NEGOCIO). Si SAP no
    /// responde se devuelve null y no se guarda nada: un almacén sin negocio se mostraría como
    /// "fuera de WEBPOS", que sería incorrecto.
    /// </summary>
    private async Task<List<PosSyncWarehouse>?> ResolveWarehousesAsync(string docType, IReadOnlyList<string> codes, CancellationToken ct)
    {
        try
        {
            var posInfo = docType == PosSyncDocTypes.GoodsReceipt
                ? await _sapGateway.GetWarehousesPosInfoAsync(codes, ct)
                : await _sapGateway.GetWarehousesPosInfoForTestCompanyAsync(codes, ct);

            return codes
                .Select(code => new PosSyncWarehouse(code, posInfo.GetValueOrDefault(code) is { IsPosEnabled: true } info ? info.CodBusiness : null))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SAP no respondió al resolver el negocio WEBPOS de los almacenes {Codes}.", string.Join(", ", codes));
            return null;
        }
    }

    private static string? NormalizeDocType(string docType) =>
        Array.Find(ValidDocTypes, t => t.Equals(docType.Trim(), StringComparison.OrdinalIgnoreCase));

    public static WebPosSyncStatusResponse ToResponse(PosSyncStatusEntry entry)
    {
        var result = entry.Status switch
        {
            "SYNCED" => WebPosIntegrationResults.Integrated,
            "SKIPPED_NOT_POS" => WebPosIntegrationResults.NotApplicable,
            "ERROR" => WebPosIntegrationResults.Error,
            _ => WebPosIntegrationResults.Rejected
        };

        var rejection = result switch
        {
            WebPosIntegrationResults.Rejected => BuildRejection(entry),
            WebPosIntegrationResults.Error => BuildError(entry),
            _ => null
        };
        var warehouses = entry.Warehouses.Select(w => new WebPosSyncWarehouse(w.Code, w.CodBusiness)).ToList();

        return new WebPosSyncStatusResponse(
            entry.DocType, entry.DocEntry, entry.DocNum, entry.Status, result, entry.Reason,
            entry.Attempts, entry.CreatedAt, entry.UpdatedAt, rejection, warehouses);
    }

    /// <summary>
    /// ERROR is not a HITELL rejection: the stock update never got through (WEBPOS down, login
    /// failed, wrong BaseUrl, timeout). Support only needs to re-send once the cause is fixed.
    /// </summary>
    private static WebPosRejectionDetail BuildError(PosSyncStatusEntry entry)
    {
        var description = "No se pudo enviar a HITELL (error de conexión o configuración, no un rechazo): " +
                          (string.IsNullOrWhiteSpace(entry.Reason) ? "sin detalle" : entry.Reason) +
                          ". Reintegrar cuando el servicio esté disponible.";
        return new WebPosRejectionDetail(WebPosRejectionTypes.Other, description, [], RetryEndpoint(entry));
    }

    private static string RetryEndpoint(PosSyncStatusEntry entry)
    {
        var resource = entry.DocType == PosSyncDocTypes.GoodsReceipt ? "goods-receipts" : "stock-transfers";
        return $"/api/v1/webpos/{resource}/{entry.DocEntry}/retry";
    }

    private static WebPosRejectionDetail BuildRejection(PosSyncStatusEntry entry)
    {
        var items = WebPosRejectionClassifier.ParseItems(entry.Reason);
        var type = WebPosRejectionClassifier.ClassifyReason(entry.Reason);

        // Tiendas donde hay que actuar: los almacenes del documento cuyo negocio WEBPOS coincide con
        // el de los ítems rechazados (HITELL rechaza por negocio, no por tienda).
        var rejectedBusinesses = items.Select(i => i.CodBusiness).Where(b => b is not null).ToHashSet();
        var stores = entry.Warehouses
            .Where(w => w.CodBusiness is not null && (rejectedBusinesses.Count == 0 || rejectedBusinesses.Contains(w.CodBusiness)))
            .Select(w => w.Code)
            .Distinct()
            .ToList();
        var storesText = stores.Count > 0 ? $" Tiendas/almacenes: {string.Join(", ", stores)}." : string.Empty;

        var description = type == WebPosRejectionTypes.ProductNotFound
            ? $"Producto no creado en HITELL: {string.Join(", ", items.Select(i => i.ItemCode ?? "?").Distinct())}.{storesText} Crear el producto en HITELL para esa(s) tienda(s) y reintegrar."
            : $"HITELL rechazó la actualización de stock.{storesText} Revisar el mensaje por ítem, corregir en HITELL y reintegrar.";

        return new WebPosRejectionDetail(type, description, items, RetryEndpoint(entry));
    }
}

/// <summary>Parámetros del listado GET /webpos/sync-status, tal como llegan del cliente (sin validar).</summary>
public sealed record WebPosSyncStatusQuery(
    string? Status,
    string? DocType,
    string? RejectionType,
    int? DocEntry,
    int? DocNum,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize);
