using System.Globalization;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Infrastructure.Configuration;
using Middleware.Infrastructure.WebPos;
using Polly.Timeout;

namespace Middleware.Infrastructure.Sap;

internal sealed class SapGateway : ISapGateway
{
    private readonly ISapSessionManager _sessionManager;
    private readonly ISapServiceLayerClient _client;
    private readonly SapThrottle _throttle;
    private readonly ItemGroupsCache _itemGroupsCache;
    private readonly WarehousePosInfoCache _warehousePosInfoCache;
    private readonly SapSettings _settings;
    private readonly ILogger<SapGateway> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string ValueProperty = "value";
    private const string DocEntryProperty = "DocEntry";
    private const string CardCodeProperty = CardCodeProperty;
    private const string DocNumProperty = "DocNum";
    private const string QuantityProperty = "Quantity";
    private const string ItemCodeProperty = "ItemCode";
    private const string WarehouseCodeProperty = "WarehouseCode";
    private const string DocumentLinesProperty = "DocumentLines";
    private const string InventoryUomProperty = "InventoryUOM";
    private const string PurchaseItemProperty = "PurchaseItem";
    private const string FrozenProperty = "Frozen";
    private const string FromWarehouseProperty = "FromWarehouse";
    private const string ToWarehouseProperty = "ToWarehouse";
    private const string StockTransferLinesProperty = "StockTransferLines";
    private const string DocDateProperty = "DocDate";
    private const string ItemNameProperty = "ItemName";
    private const string BarCodeProperty = "BarCode";
    private const string ItemWarehouseInfoCollectionProperty = "ItemWarehouseInfoCollection";
    private const string StockTransfersResource = "StockTransfers";

    public SapGateway(
        ISapSessionManager sessionManager,
        ISapServiceLayerClient client,
        SapThrottle throttle,
        ItemGroupsCache itemGroupsCache,
        WarehousePosInfoCache warehousePosInfoCache,
        IOptions<SapSettings> settings,
        ILogger<SapGateway> logger)
    {
        _sessionManager = sessionManager;
        _client = client;
        _throttle = throttle;
        _itemGroupsCache = itemGroupsCache;
        _warehousePosInfoCache = warehousePosInfoCache;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<DocumentCreatedResult> CreateStockTransferBasedOnDocumentAsync(CreateStockTransferBasedOnDocumentRequest request, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();

        var payload = JsonSerializer.Serialize(new
        {
            request.DocObjectCode,
            request.CardCode,
            request.DocDate, // NOSONAR
            request.TaxDate,
            request.Comments,
            FromWarehouse = request.FromWarehouse, // NOSONAR
            ToWarehouse = request.ToWarehouse, // NOSONAR
            request.U_ORIGEN,
            request.U_sap_id,
            request.Series,
            // PriceList -2 = "Último precio determinado" (config SAP) si el cliente no lo especifica.
            PriceList = request.PriceList ?? -2,
            StockTransferLines = request.StockTransferLines.Select(l => new
            {
                l.BaseType,
                l.BaseEntry,
                l.BaseLine,
                l.ItemCode,
                l.Quantity,
                l.WarehouseCode
            })
        }, JsonOptions);

        // Incidente 10/09/2026: SAP tardó >30 s, el POST venció, la cola reintentó 5 veces y el job
        // quedó en ERROR aunque la transferencia sí existía. Ver CreateDocumentIdempotentAsync.
        var spec = StockTransferSpec(
            _settings.TestCompanyDb, _settings.TestPassword, request.FromWarehouse, request.ToWarehouse,
            request.StockTransferLines.Select(l => (l.ItemCode, l.BaseEntry)), request.U_sap_id);
        return await CreateDocumentIdempotentAsync(request.U_sap_id, payload, spec, ct);
    }

    /// <summary>
    /// Fallos de un POST de creación tras los cuales SAP pudo haber confirmado el documento igual:
    /// timeout/cancelación o corte de conexión (la respuesta nunca llegó), o un rechazo de SAP
    /// (p. ej. 400 "Quantity cannot exceed..." porque un intento anterior ya consumió el documento
    /// base). Un circuito abierto o el throttle no envían nada a SAP, así que no aplican.
    /// </summary>
    private static bool MayHaveCreatedDocument(Exception ex) => ex switch
    {
        TimeoutRejectedException => true,
        HttpRequestException => true,
        OperationCanceledException => true,
        SapException { StatusCode: not HttpStatusCode.Unauthorized } => true,
        _ => false
    };

    /// <summary>
    /// Qué buscar y cómo reconocer "nuestro" documento al aplicar idempotencia por U_sap_id.
    /// <paramref name="CompanyDb"/> null = Sap:CompanyDb (la compañía donde escribe ese endpoint).
    /// </summary>
    private sealed record IdempotentCreateSpec(
        string Resource,
        string LookupSelect,
        string? CompanyDb,
        string? Password,
        Func<JsonElement, bool> IsSameRequest);

    /// <summary>
    /// Creación idempotente por clave U_sap_id (docs/idempotencia-documentos.md):
    ///   1. si hay clave, busca si un intento anterior ya creó el documento → lo devuelve (Recovered);
    ///   2. POST;
    ///   3. si el POST falla sin resultado concluyente, vuelve a buscar antes de propagar el error.
    /// Sin clave (módulo que aún no la envía) es exactamente el POST de siempre.
    /// </summary>
    private async Task<DocumentCreatedResult> CreateDocumentIdempotentAsync(
        string? uSapId, string payload, IdempotentCreateSpec spec, CancellationToken ct)
    {
        var hasKey = !string.IsNullOrWhiteSpace(uSapId);
        if (hasKey)
        {
            var existing = await FindDocumentForKeyAsync(uSapId!, spec, ct);
            if (existing is not null)
            {
                _logger.LogWarning( // NOSONAR
                    "{Resource} con U_sap_id={USapId} ya existe en SAP (DocEntry={DocEntry}, DocNum={DocNum}); no se crea de nuevo.",
                    spec.Resource, uSapId, existing.DocEntry, existing.DocNum);
                return existing with { Recovered = true };
            }
        }

        Exception inconclusive;
        try
        {
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, spec.Resource, payload, spec.CompanyDb, spec.Password, ct);
            return ParseDocumentResult(json);
        }
        catch (SapException ex) when (hasKey && IsUnknownUdfError(ex))
        {
            // El UDF no existe en esa tabla/compañía. La clave nunca debe impedir crear el documento:
            // se reintenta sin ella (comportamiento anterior) y se deja un error visible para crear el UDF.
            _logger.LogError(ex, // NOSONAR
                "SAP rechazó U_sap_id en {Resource} (compañía {CompanyDb}): el UDF no existe ahí. Se crea SIN clave de idempotencia. Crear el UDF sap_id (alfanumérico 60) en esa tabla.",
                spec.Resource, spec.CompanyDb ?? "Sap:CompanyDb");
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, spec.Resource, RemoveUSapId(payload), spec.CompanyDb, spec.Password, ct);
            return ParseDocumentResult(json);
        }
        catch (Exception ex) when (hasKey && MayHaveCreatedDocument(ex))
        {
            // El fallo no dice si SAP creó el documento; se resuelve fuera del catch (S2139: no
            // registrar y relanzar dentro del mismo bloque).
            inconclusive = ex;
        }

        _logger.LogWarning(inconclusive,
            "SAP POST {Resource} falló para U_sap_id={USapId} sin resultado concluyente. Verificando si el documento se creó...",
            spec.Resource, uSapId);

        var recovered = await FindDocumentForKeyAsync(uSapId!, spec, ct);
        if (recovered is not null)
        {
            _logger.LogWarning( // NOSONAR
                "Recuperado {Resource} creado por SAP pese al error: DocEntry={DocEntry}, DocNum={DocNum}, U_sap_id={USapId}",
                spec.Resource, recovered.DocEntry, recovered.DocNum, uSapId);
            return recovered with { Recovered = true };
        }

        // Relanza conservando el stack original (equivale al `throw;` que antes vivía en el catch).
        ExceptionDispatchInfo.Capture(inconclusive).Throw();
        throw inconclusive;
    }

    /// <summary>SAP 400 que menciona el propio campo: "Property 'U_sap_id' of 'Document' is invalid" → el UDF no existe.</summary>
    private static bool IsUnknownUdfError(SapException ex)
        => ex.StatusCode == HttpStatusCode.BadRequest && ex.SapError.Contains("U_sap_id", StringComparison.OrdinalIgnoreCase);

    private static string RemoveUSapId(string payload)
    {
        var node = JsonNode.Parse(payload)?.AsObject() ?? throw new SapResponseException("Payload de creación inválido al quitar U_sap_id.");
        node.Remove("U_sap_id");
        return node.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// Busca el documento con esa clave y comprueba que sea del MISMO pedido. La clave la pone el
    /// módulo llamador y SAP no valida unicidad, así que dos módulos podrían chocar: si el documento
    /// encontrado es de otro pedido se lanza <see cref="IdempotencyKeyConflictException"/> en vez de
    /// devolver un documento ajeno. Devuelve null si no hay ninguno o si SAP no respondió a la
    /// consulta (la creación sigue su curso).
    /// </summary>
    private async Task<DocumentCreatedResult?> FindDocumentForKeyAsync(string uSapId, IdempotentCreateSpec spec, CancellationToken ct)
    {
        JsonDocument doc;
        try
        {
            var escaped = uSapId.Replace("'", "''");
            var resource = $"{spec.Resource}?$filter=U_sap_id eq '{escaped}'&$select={spec.LookupSelect}&$orderby=DocEntry desc&$top=1";
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, spec.CompanyDb, spec.Password, ct);
            doc = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, // NOSONAR
                "No se pudo consultar SAP ({Resource}) por U_sap_id={USapId}; se continúa sin verificación previa", spec.Resource, uSapId);
            return null;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty(ValueProperty, out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in arr.EnumerateArray())
            {
                var docEntry = IntOrZero(item, DocEntryProperty);
                if (docEntry == 0)
                {
                    continue;
                }

                var docNum = IntOrZero(item, DocNumProperty);
                if (!spec.IsSameRequest(item))
                {
                    throw new IdempotencyKeyConflictException(uSapId, docEntry, docNum);
                }

                return new DocumentCreatedResult(docEntry, docNum);
            }

            return null;
        }
    }

    /// <summary>
    /// StockTransfers (OWTR) e InventoryTransferRequests (OWTQ): mismo pedido = mismas bodegas
    /// origen/destino y mismo documento base (sin base, mismos artículos).
    /// </summary>
    private IdempotentCreateSpec StockTransferSpec(
        string? companyDb, string? password, string fromWarehouse, string toWarehouse,
        IEnumerable<(string ItemCode, int? BaseEntry)> lines, string? uSapId, string resource = StockTransfersResource)
    {
        var requestLines = lines.ToList();
        return new IdempotentCreateSpec(
            resource,
            "DocEntry,DocNum,FromWarehouse,ToWarehouse,StockTransferLines",
            companyDb,
            password,
            found =>
                string.Equals(StringOrEmpty(found, FromWarehouseProperty), fromWarehouse, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(StringOrEmpty(found, ToWarehouseProperty), toWarehouse, StringComparison.OrdinalIgnoreCase) &&
                HasSameOrigin(found, StockTransferLinesProperty, requestLines, uSapId));
    }

    /// <summary>
    /// Mismo origen = cada BaseEntry del request aparece en las líneas del documento encontrado (sin
    /// documento base se compara por artículos). A propósito NO exige las mismas líneas ni
    /// cantidades: recepción omite líneas en cero y el usuario puede haber escaneado algo más entre
    /// el intento que venció y el reintento; el documento de SAP ya existe y no admite cambios, así
    /// que devolverlo es lo correcto (se deja constancia en el log).
    /// </summary>
    private bool HasSameOrigin(JsonElement found, string linesProperty, IReadOnlyList<(string ItemCode, int? BaseEntry)> requestLines, string? uSapId)
    {
        var foundBaseEntries = new HashSet<int>();
        var foundItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (found.TryGetProperty(linesProperty, out var linesEl) && linesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in linesEl.EnumerateArray())
            {
                if (NullableInt(line, "BaseEntry") is int baseEntry && baseEntry > 0)
                {
                    foundBaseEntries.Add(baseEntry);
                }

                foundItems.Add(StringOrEmpty(line, ItemCodeProperty).Trim());
            }
        }

        var requestBaseEntries = requestLines.Where(l => l.BaseEntry is > 0).Select(l => l.BaseEntry!.Value).ToHashSet();
        var sameOrigin = requestBaseEntries.Count > 0
            ? requestBaseEntries.IsSubsetOf(foundBaseEntries)
            : requestLines.All(l => foundItems.Contains(l.ItemCode.Trim()));
        if (!sameOrigin)
        {
            return false;
        }

        var requestItems = requestLines.Select(l => l.ItemCode.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!requestItems.SetEquals(foundItems))
        {
            _logger.LogWarning( // NOSONAR
                "Documento con U_sap_id={USapId} ya existe en SAP con artículos distintos al reintento (SAP: {FoundCount}, request: {RequestCount}); se devuelve el existente, SAP no permite modificarlo.",
                uSapId, foundItems.Count, requestItems.Count);
        }

        return true;
    }

    private static int? NullableInt(JsonElement element, string property)
        => element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v : null;

    public async Task<DocumentCreatedResult> CreateStockTransferForCompanyAsync(CreateStockTransferBasedOnDocumentRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            request.DocObjectCode,
            request.CardCode,
            request.DocDate, // NOSONAR
            request.TaxDate,
            request.Comments,
            FromWarehouse = request.FromWarehouse, // NOSONAR
            ToWarehouse = request.ToWarehouse, // NOSONAR
            request.U_ORIGEN,
            request.U_sap_id,
            request.Series,
            PriceList = request.PriceList ?? -2,
            StockTransferLines = request.StockTransferLines.Select(l => new
            {
                l.BaseType,
                l.BaseEntry,
                l.BaseLine,
                l.ItemCode,
                l.Quantity,
                l.WarehouseCode
            })
        }, JsonOptions);

        // Sin companyDb/password: usa Sap:CompanyDb / Sap:Password (la compañía real), NO
        // Sap:TestCompanyDb — a diferencia de CreateStockTransferBasedOnDocumentAsync de
        // arriba (ese es del sandbox de WebPOS). Idempotencia solo si el módulo envía u_sap_id
        // (el controller no genera clave aquí: el UDF debe existir en esa compañía).
        var spec = StockTransferSpec(
            companyDb: null, password: null, request.FromWarehouse, request.ToWarehouse,
            request.StockTransferLines.Select(l => (l.ItemCode, l.BaseEntry)), request.U_sap_id);
        return await CreateDocumentIdempotentAsync(request.U_sap_id, payload, spec, ct);
    }

    public async Task<DocumentCreatedResult> CreateStockTransferDirectAsync(CreateStockTransferDirectRequest request, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();

        var payload = JsonSerializer.Serialize(new
        {
            request.DocObjectCode,
            request.CardCode,
            request.DocDate, // NOSONAR
            request.TaxDate,
            request.Comments,
            FromWarehouse = request.FromWarehouse, // NOSONAR
            ToWarehouse = request.ToWarehouse, // NOSONAR
            request.U_ORIGEN,
            request.U_sap_id,
            request.U_GUIA_LAAR,
            request.Series,
            // PriceList -2 = "Último precio determinado" (config SAP) si el cliente no lo especifica.
            PriceList = request.PriceList ?? -2,
            StockTransferLines = request.StockTransferLines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                l.WarehouseCode
            })
        }, JsonOptions);

        try
        {
            var json = await ExecuteWithRetryOn401Async(
                HttpMethod.Post, StockTransfersResource, payload, _settings.TestCompanyDb, _settings.TestPassword, ct);
            return ParseDocumentResult(json);
        }
        catch (TimeoutRejectedException) when (!string.IsNullOrEmpty(request.U_sap_id))
        {
            _logger.LogWarning( // NOSONAR
                "SAP POST StockTransfers timed out for U_sap_id={USapId}. Checking for orphaned transfer...",
                request.U_sap_id);

            var recovered = await FindStockTransferByCorrelationIdAsync(request.U_sap_id, ct);
            if (recovered is not null)
            {
                _logger.LogWarning( // NOSONAR
                    "Recovered orphaned StockTransfer: DocEntry={DocEntry}, DocNum={DocNum}, U_sap_id={USapId}",
                    recovered.DocEntry, recovered.DocNum, request.U_sap_id);
                return recovered with { Recovered = true };
            }
            throw;
        }
    }

    private async Task<DocumentCreatedResult?> FindStockTransferByCorrelationIdAsync(string uSapId, CancellationToken ct)
    {
        try
        {
            return await GetStockTransferByCorrelationIdAsync(uSapId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not query SAP for orphaned stock transfer — U_sap_id={USapId}",
                uSapId);
            return null;
        }
    }


    public async Task<DocumentCreatedResult?> GetStockTransferByCorrelationIdAsync(string uSapId, CancellationToken ct = default)
    {
        // Los StockTransfers con U_sap_id los crean /direct y /based-on-document, y ambos escriben
        // en Sap:TestCompanyDb — la búsqueda tiene que ir a esa misma compañía. Antes consultaba
        // Sap:CompanyDb, así que en un deploy donde ambas difieren nunca encontraba nada y la
        // recuperación tras timeout de /direct no servía.
        EnsureTestCredentialsConfigured();

        var escaped = uSapId.Replace("'", "''");
        var resource = $"StockTransfers?$filter=U_sap_id eq '{escaped}'&$select=DocEntry,DocNum&$orderby=DocEntry desc&$top=1";

        var json = await ExecuteWithRetryOn401Async(
            HttpMethod.Get, resource, null, _settings.TestCompanyDb, _settings.TestPassword, ct);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(ValueProperty, out var arr))
        {
            return null;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty(DocEntryProperty, out var de) && de.TryGetInt32(out var docEntry) &&
                item.TryGetProperty(DocNumProperty, out var dn) && dn.TryGetInt32(out var docNum))
            {
                return new DocumentCreatedResult(docEntry, docNum);
            }
        }

        return null;
    }


    public async Task<DocumentCreatedResult> CreateStockTransferWithBinsAsync(CreateStockTransferWithBinsRequest request, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();

        var payload = JsonSerializer.Serialize(new
        {
            request.DocObjectCode,
            request.CardCode,
            request.DocDate, // NOSONAR
            request.TaxDate,
            request.Comments,
            FromWarehouse = request.FromWarehouse, // NOSONAR
            ToWarehouse = request.ToWarehouse, // NOSONAR
            request.U_ORIGEN,
            request.U_sap_id,
            request.Series,
            // PriceList -2 = "Último precio determinado" (config SAP) si el cliente no lo especifica.
            PriceList = request.PriceList ?? -2,
            StockTransferLines = request.StockTransferLines.Select(l => new
            {
                l.BaseType,
                l.BaseEntry,
                l.BaseLine,
                l.ItemCode,
                l.Quantity,
                l.WarehouseCode,
                // Null si esa línea no trae bins (bodega sin ubicaciones en ese lado) — con
                // DefaultIgnoreCondition.WhenWritingNull el campo se omite del payload a SAP.
                StockTransferLinesBinAllocations = l.StockTransferLinesBinAllocations?.Select(b => new
                {
                    b.BinAbsEntry,
                    b.Quantity,
                    AllocationMethod = "bamByBinCode",
                    // 1 = Entrada (bin destino) → batToWarehouse; 2 = Salida (bin origen) → batFromWarehouse.
                    BinActionType = b.BinActionType == 1 ? "batToWarehouse" : "batFromWarehouse" // NOSONAR
                })
            })
        }, JsonOptions);

        var spec = StockTransferSpec(
            _settings.TestCompanyDb, _settings.TestPassword, request.FromWarehouse, request.ToWarehouse,
            request.StockTransferLines.Select(l => (l.ItemCode, l.BaseEntry)), request.U_sap_id);
        return await CreateDocumentIdempotentAsync(request.U_sap_id, payload, spec, ct);
    }

    public async Task<StockTransferStatusResponse?> GetStockTransferStatusAsync(int docEntry, CancellationToken ct = default)
    {
        string json;
        try
        {
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"StockTransfers({docEntry})?$select=DocEntry,DocNum,FromWarehouse,ToWarehouse,StockTransferLines", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP StockTransfer DocEntry={DocEntry} not found", docEntry); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int GetInt(string f) => root.TryGetProperty(f, out var p) && p.TryGetInt32(out var v) ? v : 0;
        string GetStr(string f) => root.TryGetProperty(f, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : string.Empty;

        return new StockTransferStatusResponse(
            GetInt(DocEntryProperty), GetInt(DocNumProperty), GetStr(FromWarehouseProperty), GetStr(ToWarehouseProperty),
            ParseStockTransferLines(root));
    }

    private static List<StockTransferLineStatusResponse> ParseStockTransferLines(JsonElement root)
    {
        var lines = new List<StockTransferLineStatusResponse>();
        if (!root.TryGetProperty(StockTransferLinesProperty, out var linesEl))
        {
            return lines;
        }

        foreach (var line in linesEl.EnumerateArray())
        {
            string LineStr(string f) => line.TryGetProperty(f, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : string.Empty;
            decimal LineDec(string f) => line.TryGetProperty(f, out var p) && p.TryGetDecimal(out var v) ? v : 0m;

            lines.Add(new StockTransferLineStatusResponse(
                LineStr("ItemCode"),
                LineStr("ItemDescription"),
                LineDec(QuantityProperty),
                LineDec("RemainingOpenQuantity")
            ));
        }

        return lines;
    }


    private void EnsureTestCredentialsConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.TestCompanyDb))
        {
            throw new InvalidOperationException("Sap:TestCompanyDb no está configurado.");
        }

        if (string.IsNullOrWhiteSpace(_settings.TestPassword))
        {
            throw new InvalidOperationException("Sap:TestPassword no está configurado.");
        }
    }

    public async Task<DocumentCreatedResult> CreateStockTransferRequestAsync(CreateStockTransferRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            DocDate = DateTime.Now.ToString("yyyy-MM-dd"), // NOSONAR
            request.Comments,
            // PriceList -2 = "Último precio determinado" (config SAP). El flujo Python que
            // funcionaba lo envía, así que se replica para paridad de payload. No quitar.
            PriceList = -2,
            FromWarehouse = request.FromWarehouse, // NOSONAR
            ToWarehouse = request.ToWarehouse, // NOSONAR
            // U_ODC (UDF con el nº de OC de origen) y Series los incluye el payload del flujo
            // Python que sí funciona. U_ODC es el candidato más probable del error 1299
            // "no data found" de la TN custom (hace lookup contra la OC). Se omiten solos si
            // el request no los trae (WhenWritingNull).
            U_ODC = request.UOdc,
            Series = request.Series,
            // Tag de sistema origen (mismo UDF que el resto de los Create*Request); opcional,
            // se omite si no viene.
            request.U_ORIGEN,
            // Clave de idempotencia (UDF U_sap_id de OWTQ, 60): la del módulo o el GUID del controller.
            request.U_sap_id,
            // ITR: la bodega va normalmente SOLO en la cabecera (FromWarehouse/ToWarehouse). El
            // flujo Python que sí funciona manda únicamente ItemCode/Quantity por línea (sin
            // WarehouseCode, que queda null y se omite -- WhenWritingNull); queda disponible para
            // quien necesite un destino distinto por línea, igual que en las variantes Direct/
            // BasedOnDocument/WithBins.
            StockTransferLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                l.ProjectCode,
                l.WarehouseCode
            })
        }, JsonOptions);

        // Idempotencia por U_sap_id (UDF existente en OWTQ). Una solicitud no tiene documento base:
        // mismo pedido = mismas bodegas y mismos artículos.
        var spec = StockTransferSpec(
            companyDb: null, password: null, request.FromWarehouse, request.ToWarehouse,
            request.Lines.Select(l => (l.ItemCode, (int?)null)), request.U_sap_id, resource: "InventoryTransferRequests");
        return await CreateDocumentIdempotentAsync(request.U_sap_id, payload, spec, ct);
    }

    public async Task<DocumentCreatedResult> CreateDepositAsync(CreateDepositRequest request, string companyDb, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            DocDate = request.DocDate.ToString("yyyy-MM-dd"),
            request.Comments,
            request.U_sap_id,
            DocumentLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                WarehouseCode = l.WarehouseCode // NOSONAR
            })
        }, JsonOptions);

        // Idempotencia solo si el módulo envía u_sap_id (requiere el UDF U_sap_id en documentos de
        // marketing de esa compañía). Mismo pedido = mismos artículos por bodega.
        var requestLines = request.Lines.Select(l => (l.ItemCode, l.WarehouseCode)).ToList();
        var spec = new IdempotentCreateSpec(
            "InventoryGenEntries", "DocEntry,DocNum,DocumentLines", null, null,
            found => HasSameItemsPerWarehouse(found, requestLines));
        return await CreateDocumentIdempotentAsync(request.U_sap_id, payload, spec, ct);
    }

    // Object type de SAP B1 para Purchase Orders — lo que marca un GoodsReceipt como
    // "Copy From" de una PO puntual (reduce su pendiente y la deja cerrarse sola) en vez de
    // un documento suelto sin relación con ninguna orden de compra.
    private const int SapPurchaseOrdersObjectType = 22;

    public async Task<DocumentCreatedResult> CreateGoodsReceiptAsync(CreateGoodsReceiptRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            CardCode = request.VendorCode,
            DocDate = request.DocDate.ToString("yyyy-MM-dd"),
            request.Comments,
            request.U_sap_id,
            DocumentLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                l.UnitPrice,
                WarehouseCode = l.WarehouseCode, // NOSONAR
                BaseType = l.BaseEntry.HasValue ? SapPurchaseOrdersObjectType : (int?)null,
                l.BaseEntry,
                l.BaseLine
            })
        }, JsonOptions);

        // Idempotencia solo si el módulo envía u_sap_id (requiere el UDF U_sap_id en documentos de
        // marketing). Mismo pedido = mismo proveedor y misma(s) orden(es) de compra base (o mismos
        // artículos si la entrada no referencia órdenes).
        var requestLines = request.Lines.Select(l => (l.ItemCode, l.BaseEntry)).ToList();
        var spec = new IdempotentCreateSpec(
            "PurchaseDeliveryNotes", "DocEntry,DocNum,CardCode,DocumentLines", null, null,
            found => string.Equals(StringOrEmpty(found, CardCodeProperty), request.VendorCode, StringComparison.OrdinalIgnoreCase)
                     && HasSameOrigin(found, DocumentLinesProperty, requestLines, request.U_sap_id));
        return await CreateDocumentIdempotentAsync(request.U_sap_id, payload, spec, ct);
    }

    /// <summary>Depósitos: mismo pedido = cada (artículo, bodega) del request está en el documento encontrado.</summary>
    private static bool HasSameItemsPerWarehouse(JsonElement found, IReadOnlyList<(string ItemCode, string WarehouseCode)> requestLines)
    {
        var foundLines = new HashSet<(string, string)>();
        if (found.TryGetProperty(DocumentLinesProperty, out var linesEl) && linesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in linesEl.EnumerateArray())
            {
                foundLines.Add((StringOrEmpty(line, ItemCodeProperty).Trim().ToUpperInvariant(),
                                StringOrEmpty(line, WarehouseCodeProperty).Trim().ToUpperInvariant()));
            }
        }

        return requestLines.All(l => foundLines.Contains((l.ItemCode.Trim().ToUpperInvariant(), l.WarehouseCode.Trim().ToUpperInvariant())));
    }

    public async Task<DocumentCreatedResult> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            CardCode = request.CardCode, // NOSONAR
            DocDate = request.DocDate.ToString("yyyy-MM-dd"),
            DocDueDate = request.DocDueDate.ToString("yyyy-MM-dd"),
            TaxDate = request.TaxDate.ToString("yyyy-MM-dd"),
            request.Comments,
            U_ORIGEN = request.U_ORIGEN, // NOSONAR
            U_USUARIO = request.U_USUARIO, // NOSONAR
            U_FECHA = request.U_FECHA?.ToString("yyyy-MM-dd"),
            U_FE_ETG = request.U_FE_ETG?.ToString("yyyy-MM-dd"),
            U_LOTE = request.U_LOTE, // NOSONAR
            request.U_sap_id,
            DocumentLines = request.Lines.Select(l => new
            {
                l.ItemCode,
                l.Quantity,
                l.UnitPrice,
                WarehouseCode = l.WarehouseCode // NOSONAR
            })
        }, JsonOptions);

        // Idempotencia por U_sap_id (UDF existente en OPOR, 60). Mismo pedido = mismo proveedor y
        // mismos artículos. Sustituye la recuperación anterior por CardCode + Comments, que no era
        // única ni servía de verificación previa.
        var requestLines = request.Lines.Select(l => (l.ItemCode, (int?)null)).ToList();
        var spec = new IdempotentCreateSpec(
            "PurchaseOrders", "DocEntry,DocNum,CardCode,DocumentLines", null, null,
            found => string.Equals(StringOrEmpty(found, CardCodeProperty), request.CardCode, StringComparison.OrdinalIgnoreCase)
                     && HasSameOrigin(found, DocumentLinesProperty, requestLines, request.U_sap_id));
        return await CreateDocumentIdempotentAsync(request.U_sap_id, payload, spec, ct);
    }

    // Ported 1:1 from API_SLAYER's SapService.CreatePurchaseLiquidationAsync — same SAP resource
    // (PurchaseInvoices), same field defaults, same server-stamped dates and AccountCode fixup.
    public async Task<DocumentCreatedResult> CreateLiquidacionAsync(CreateLiquidacionRequest request, CancellationToken ct = default)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd"); // NOSONAR

        var payload = new Dictionary<string, object?>
        {
            [CardCodeProperty] = request.CardCode,
            ["DocType"] = NormalizeLiquidacionText(request.DocType) ?? "dDocument_Service",
            [DocDateProperty] = today,
            ["DocDueDate"] = today,
            ["NumAtCard"] = string.Empty,
            ["Comments"] = NormalizeLiquidacionText(request.Comments) ?? "LIQUIDACION DE COMPRA - API",
            ["U_COD_ST"] = NormalizeLiquidacionText(request.U_COD_ST) ?? "01",
            ["U_tipo_comprob"] = NormalizeLiquidacionText(request.U_tipo_comprob) ?? "03",
            ["U_ORIGEN"] = NormalizeLiquidacionText(request.U_ORIGEN) ?? "C",
            ["U_SER_EST"] = NormalizeLiquidacionText(request.U_SER_EST) ?? "001",
            ["U_SER_PE"] = NormalizeLiquidacionText(request.U_SER_PE) ?? "100"
        };

        if (request.Series.HasValue)
        {
            payload["Series"] = request.Series.Value;
        }

        var resolvedDocCurrency = NormalizeLiquidacionText(request.DocCurrency);
        if (!string.IsNullOrWhiteSpace(resolvedDocCurrency))
        {
            payload["DocCurrency"] = resolvedDocCurrency;
        }

        var resolvedJournalMemo = NormalizeLiquidacionText(request.JournalMemo);
        if (!string.IsNullOrWhiteSpace(resolvedJournalMemo))
        {
            payload["JournalMemo"] = resolvedJournalMemo;
        }

        if (request.U_STATUS.HasValue)
        {
            payload["U_STATUS"] = request.U_STATUS.Value;
        }

        payload[DocumentLinesProperty] = (request.DocumentLines ?? []).Select(line =>
        {
            var node = new Dictionary<string, object?>
            {
                ["AccountCode"] = NormalizeLiquidacionAccountCode(line.AccountCode),
                ["ItemDescription"] = line.ItemDescription,
                [QuantityProperty] = line.Quantity,
                ["UnitPrice"] = line.UnitPrice,
                ["TaxCode"] = line.TaxCode,
                ["CostingCode"] = line.CostingCode,
                ["CostingCode2"] = line.CostingCode2,
                ["CostingCode3"] = line.CostingCode3
            };

            if (!string.IsNullOrWhiteSpace(line.CostingCode4))
            {
                node["CostingCode4"] = line.CostingCode4;
            }

            if (!string.IsNullOrWhiteSpace(line.CostingCode5))
            {
                node["CostingCode5"] = line.CostingCode5;
            }

            return node;
        }).ToList();

        if (!string.IsNullOrWhiteSpace(request.U_sap_id))
        {
            payload["U_sap_id"] = request.U_sap_id;
        }

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        _logger.LogInformation("JSON final liquidacion antes de enviar a SAP:\n{Payload}", payloadJson); // NOSONAR

        // Idempotencia por U_sap_id (UDF existente en OPCH, 60). Una liquidación es un documento de
        // servicio sin artículos: mismo pedido = mismo socio de negocios y mismas cuentas contables.
        var requestAccounts = (request.DocumentLines ?? [])
            .Select(l => NormalizeLiquidacionAccountCode(l.AccountCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var spec = new IdempotentCreateSpec(
            "PurchaseInvoices", "DocEntry,DocNum,CardCode,DocumentLines", null, null,
            found => string.Equals(StringOrEmpty(found, CardCodeProperty), request.CardCode, StringComparison.OrdinalIgnoreCase)
                     && HasSameAccounts(found, requestAccounts));

        try
        {
            return await CreateDocumentIdempotentAsync(request.U_sap_id, payloadJson, spec, ct);
        }
        catch (SapException ex) when (IsLiquidacionAccountCodeError(ex.SapError))
        {
            throw new SapException(ex.StatusCode,
                ex.SapError + " | Verifique que AccountCode corresponda al codigo interno OACT.AcctCode (ej: _SYS...).");
        }
    }

    /// <summary>Liquidaciones: cada cuenta contable del request aparece en las líneas del documento encontrado.</summary>
    private static bool HasSameAccounts(JsonElement found, HashSet<string> requestAccounts)
    {
        var foundAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (found.TryGetProperty(DocumentLinesProperty, out var linesEl) && linesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in linesEl.EnumerateArray())
            {
                foundAccounts.Add(StringOrEmpty(line, "AccountCode").Trim());
            }
        }

        return requestAccounts.IsSubsetOf(foundAccounts);
    }

    // "string" / "__TODAY__" / "__DOCNO__" are Swagger/legacy-client placeholder values, treated as absent.
    private static string? NormalizeLiquidacionText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals("string", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("__TODAY__", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("__DOCNO__", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    // SAP B1 with segmented accounts often uses internal AcctCode like _SYS00000000000.
    private static string NormalizeLiquidacionAccountCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("SYS", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("_SYS", StringComparison.OrdinalIgnoreCase))
        {
            return $"_{trimmed}";
        }

        return trimmed;
    }

    private static bool IsLiquidacionAccountCodeError(string sapError)
    {
        try
        {
            var errorObj = JsonSerializer.Deserialize<JsonElement>(sapError);
            var code = errorObj.TryGetProperty("error", out var errorProp) && errorProp.TryGetProperty("code", out var codeProp)
                ? codeProp.ToString() : "";
            var message = errorObj.TryGetProperty("error", out var errorProp2) && errorProp2.TryGetProperty("message", out var msgProp) && msgProp.TryGetProperty(ValueProperty, out var valProp)
                ? valProp.ToString() : "";
            return code == "-2028" && message.Contains("DocumentLines.AccountCode", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<PurchaseOrderResponse?> GetPurchaseOrderAsync(int docEntry, CancellationToken ct = default)
    {
        string json;
        try
        {
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get, $"PurchaseOrders({docEntry})", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP PurchaseOrder DocEntry={DocEntry} not found (SAP 404)", docEntry); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int RequiredInt(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.TryGetInt32(out var v))
            {
                return v;
            }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de PurchaseOrder DocEntry={docEntry}");
        }
        string RequiredString(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String)
            {
                return p.GetString()!;
            }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de PurchaseOrder DocEntry={docEntry}");
        }
        decimal RequiredDecimal(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.TryGetDecimal(out var v))
            {
                return v;
            }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de PurchaseOrder DocEntry={docEntry}");
        }

        var lines = root.TryGetProperty(DocumentLinesProperty, out var linesEl)
            ? linesEl.EnumerateArray()
                .Select((l, i) => new PurchaseOrderLineResponse(
                    RequiredInt(l, "LineNum", $"DocumentLines[{i}]"),
                    RequiredString(l, ItemCodeProperty, $"DocumentLines[{i}]"),
                    RequiredDecimal(l, QuantityProperty, $"DocumentLines[{i}]"),
                    // El nombre real en SAP Service Layer es "RemainingOpenQuantity" —
                    // "OpenQuantity" no existe en este esquema y hacía fallar cualquier
                    // PurchaseOrder con líneas reales (ver hallazgo de api-cedi, mismo bug).
                    RequiredDecimal(l, "RemainingOpenQuantity", $"DocumentLines[{i}]"),
                    RequiredDecimal(l, "UnitPrice", $"DocumentLines[{i}]"),
                    RequiredString(l, WarehouseCodeProperty, $"DocumentLines[{i}]")
                )).ToList()
            : (IReadOnlyList<PurchaseOrderLineResponse>)[];

        return new PurchaseOrderResponse(
            RequiredInt(root, DocEntryProperty, "root"),
            RequiredInt(root, DocNumProperty, "root"),
            RequiredString(root, CardCodeProperty, "root"),
            RequiredString(root, DocDateProperty, "root"),
            RequiredString(root, "DocumentStatus", "root"),
            lines
        );
    }

    public async Task<PurchaseOrdersListResponse> GetPurchaseOrdersAsync(GetPurchaseOrdersRequest request, CancellationToken ct = default)
    {
        var resource = $"PurchaseOrders?$top={request.Top}&$skip={request.Skip}&$orderby=DocEntry desc";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        var items = node?[ValueProperty]?.AsArray()
            .OfType<System.Text.Json.Nodes.JsonObject>()
            .ToList()
            ?? [];

        return new PurchaseOrdersListResponse(items, items.Count);
    }

    public async Task UpdatePurchaseOrderAsync(int docEntry, UpdatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            DocumentLines = request.Lines.Select(l => new
            {
                l.LineNum,
                l.Quantity,
                l.WarehouseCode
            })
        }, JsonOptions);

        try
        {
            await ExecuteWithRetryOn401Async(HttpMethod.Patch, $"PurchaseOrders({docEntry})", payload, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP PurchaseOrder DocEntry={DocEntry} not found (SAP 404)", docEntry); // NOSONAR
            throw;
        }
    }

    public async Task UpdatePurchaseOrderStatusAsync(int docEntry, string status, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            U_STATUS = status // NOSONAR
        }, JsonOptions);

        try
        {
            await ExecuteWithRetryOn401Async(HttpMethod.Patch, $"PurchaseOrders({docEntry})", payload, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP PurchaseOrder DocEntry={DocEntry} not found (SAP 404)", docEntry); // NOSONAR
            throw;
        }
    }

    public async Task CreateItemAsync(CreateItemRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(BuildItemPayload(request, request.ItemCode, request.ItemName), JsonOptions);
        await ExecuteWithRetryOn401Async(HttpMethod.Post, "Items", payload, ct);
    }

    public async Task UpdateItemAsync(string itemCode, UpdateItemRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(BuildItemPayload(request, itemCode: null, request.ItemName), JsonOptions);
        var encoded = Uri.EscapeDataString(itemCode);

        try
        {
            await ExecuteWithRetryOn401Async(HttpMethod.Patch, $"Items('{encoded}')", payload, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP Item '{ItemCode}' not found (SAP 404)", itemCode); // NOSONAR
            throw;
        }
    }

    public async Task DeleteItemAsync(string itemCode, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(itemCode);
        try
        {
            await ExecuteWithRetryOn401Async(HttpMethod.Delete, $"Items('{encoded}')", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP Item '{ItemCode}' not found (SAP 404)", itemCode); // NOSONAR
            throw;
        }
    }

    // Shared by Create/UpdateItemAsync: both DTOs carry the same optional SAP-facing fields,
    // only ItemCode (omitted from the PATCH body — it is already in the URL) and ItemName's
    // required-ness differ between the two callers.
    private static Dictionary<string, object?> BuildItemPayload(object request, string? itemCode, string? itemName)
    {
        var (fields, itemPrices) = request switch
        {
            CreateItemRequest c => (ExtractItemFields(c), c.ItemPrices),
            UpdateItemRequest u => (ExtractItemFields(u), u.ItemPrices),
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };

        var payload = new Dictionary<string, object?>(fields);
        if (itemCode is not null)
        {
            payload["ItemCode"] = itemCode;
        }
        if (itemName is not null)
        {
            payload[ItemNameProperty] = itemName;
        }
        if (itemPrices is { Count: > 0 })
        {
            payload["ItemPrices"] = itemPrices.Select(p => new { p.PriceList, p.Price, p.Currency });
        }

        // Dictionary<TKey,TValue> serialization does not honour DefaultIgnoreCondition.WhenWritingNull —
        // strip unset fields explicitly so a PATCH only carries the properties the caller actually set.
        var dict = payload.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);

        // Convert explicitly sent empty strings ("") to actual nulls for OData Service Layer.
        // This allows Python to send {"ValidFrom": ""} and ensures SAP SL receives {"ValidFrom": null}
        // to properly clear an optional property without breaking the global null-stripping logic.
        foreach (var key in dict.Keys.ToList())
        {
            if (dict[key] is string s && s == "")
            {
                dict[key] = null;
            }
        }

        return dict;
    }

    private static Dictionary<string, object?> ExtractItemFields(CreateItemRequest r) => new()
    {
        ["ForeignName"] = r.ForeignName,
        [BarCodeProperty] = r.BarCode,
        [InventoryUomProperty] = r.InventoryUOM,
        ["PurchaseUnit"] = r.PurchaseUnit,
        ["PurchaseItemsPerUnit"] = r.PurchaseItemsPerUnit,
        ["SalesUnit"] = r.SalesUnit,
        ["SalesItemsPerUnit"] = r.SalesItemsPerUnit,
        ["ItemsGroupCode"] = r.ItemsGroupCode,
        ["ItemType"] = r.ItemType,
        ["CostAccountingMethod"] = r.CostAccountingMethod,
        ["GLMethod"] = r.GLMethod,
        ["ManageStockByWarehouse"] = r.ManageStockByWarehouse,
        [PurchaseItemProperty] = r.PurchaseItem,
        ["SalesItem"] = r.SalesItem,
        ["InventoryItem"] = r.InventoryItem,
        ["VatLiable"] = r.VatLiable,
        ["WTLiable"] = r.WTLiable,
        ["IndirectTax"] = r.IndirectTax,
        ["ArTaxCode"] = r.ArTaxCode,
        ["ApTaxCode"] = r.ApTaxCode,
        ["Manufacturer"] = r.Manufacturer,
        ["ShipType"] = r.ShipType,
        ["ManageBatchNumbers"] = r.ManageBatchNumbers,
        ["PlanningSystem"] = r.PlanningSystem,
        ["ProcurementMethod"] = r.ProcurementMethod,
        [FrozenProperty] = r.Frozen,
        ["Valid"] = r.Valid,
        ["ValidFrom"] = r.ValidFrom,
        ["ValidTo"] = r.ValidTo,
        ["FrozenFrom"] = r.FrozenFrom,
        ["FrozenTo"] = r.FrozenTo,
        ["Properties63"] = r.Properties63,
        ["LeadTime"] = r.LeadTime,
        ["MinOrderQuantity"] = r.MinOrderQuantity,
        ["U_ItemCode2"] = r.U_ItemCode2,
        ["U_SXX_SKU_SIN_TALLA"] = r.U_SXX_SKU_SIN_TALLA,
        ["U_IXX_TALLA"] = r.U_IXX_TALLA,
        ["U_IXX_MARCA"] = r.U_IXX_MARCA,
        ["U_DESC_ECOMM"] = r.U_DESC_ECOMM,
        ["U_GEN"] = r.U_GEN,
        ["U_COD_COLOR"] = r.U_COD_COLOR,
        ["U_IXX_COLORES"] = r.U_IXX_COLORES,
        ["U_COD_COL_SECU"] = r.U_COD_COL_SECU,
        ["U_COLOR_SECU"] = r.U_COLOR_SECU,
        ["U_IXX_MATERIAL"] = r.U_IXX_MATERIAL,
        ["U_IXX_COMPOSICION"] = r.U_IXX_COMPOSICION,
        ["U_NV3"] = r.U_NV3,
        ["U_NV2"] = r.U_NV2,
        ["U_NV1"] = r.U_NV1,
        ["U_MARCA"] = r.U_MARCA,
        ["U_NOM_COLECCION"] = r.U_NOM_COLECCION,
        ["U_ATR_MED"] = r.U_ATR_MED,
        ["U_ATR_UNI"] = r.U_ATR_UNI,
        ["U_IXX_PACK"] = r.U_IXX_PACK,
        ["U_COD_PVR"] = r.U_COD_PVR,
        ["U_REPOSICION"] = r.U_REPOSICION,
        ["U_ORIGEN"] = r.U_ORIGEN,
        ["U_P_ORI"] = r.U_P_ORI,
        ["U_TIP_ABS"] = r.U_TIP_ABS,
        ["U_UNIDAD_DESPACHO"] = r.U_UNIDAD_DESPACHO,
        ["U_DESP_IN"] = r.U_DESP_IN,
        ["U_CANAL_VENTA"] = r.U_CANAL_VENTA,
        ["U_REGION"] = r.U_REGION,
        ["U_PR_ARQ"] = r.U_PR_ARQ,
        ["U_LIQ"] = r.U_LIQ,
        ["U_TIPO_BIEN"] = r.U_TIPO_BIEN
    };

    private static Dictionary<string, object?> ExtractItemFields(UpdateItemRequest r) => new()
    {
        ["ForeignName"] = r.ForeignName,
        [BarCodeProperty] = r.BarCode,
        [InventoryUomProperty] = r.InventoryUOM,
        ["PurchaseUnit"] = r.PurchaseUnit,
        ["PurchaseItemsPerUnit"] = r.PurchaseItemsPerUnit,
        ["SalesUnit"] = r.SalesUnit,
        ["SalesItemsPerUnit"] = r.SalesItemsPerUnit,
        ["ItemsGroupCode"] = r.ItemsGroupCode,
        ["ItemType"] = r.ItemType,
        ["CostAccountingMethod"] = r.CostAccountingMethod,
        ["GLMethod"] = r.GLMethod,
        ["ManageStockByWarehouse"] = r.ManageStockByWarehouse,
        [PurchaseItemProperty] = r.PurchaseItem,
        ["SalesItem"] = r.SalesItem,
        ["InventoryItem"] = r.InventoryItem,
        ["VatLiable"] = r.VatLiable,
        ["WTLiable"] = r.WTLiable,
        ["IndirectTax"] = r.IndirectTax,
        ["ArTaxCode"] = r.ArTaxCode,
        ["ApTaxCode"] = r.ApTaxCode,
        ["Manufacturer"] = r.Manufacturer,
        ["ShipType"] = r.ShipType,
        ["ManageBatchNumbers"] = r.ManageBatchNumbers,
        ["PlanningSystem"] = r.PlanningSystem,
        ["ProcurementMethod"] = r.ProcurementMethod,
        [FrozenProperty] = r.Frozen,
        ["Valid"] = r.Valid,
        ["ValidFrom"] = r.ValidFrom,
        ["ValidTo"] = r.ValidTo,
        ["FrozenFrom"] = r.FrozenFrom,
        ["FrozenTo"] = r.FrozenTo,
        ["Properties63"] = r.Properties63,
        ["LeadTime"] = r.LeadTime,
        ["MinOrderQuantity"] = r.MinOrderQuantity,
        ["U_ItemCode2"] = r.U_ItemCode2,
        ["U_SXX_SKU_SIN_TALLA"] = r.U_SXX_SKU_SIN_TALLA,
        ["U_IXX_TALLA"] = r.U_IXX_TALLA,
        ["U_IXX_MARCA"] = r.U_IXX_MARCA,
        ["U_DESC_ECOMM"] = r.U_DESC_ECOMM,
        ["U_GEN"] = r.U_GEN,
        ["U_COD_COLOR"] = r.U_COD_COLOR,
        ["U_IXX_COLORES"] = r.U_IXX_COLORES,
        ["U_COD_COL_SECU"] = r.U_COD_COL_SECU,
        ["U_COLOR_SECU"] = r.U_COLOR_SECU,
        ["U_IXX_MATERIAL"] = r.U_IXX_MATERIAL,
        ["U_IXX_COMPOSICION"] = r.U_IXX_COMPOSICION,
        ["U_NV3"] = r.U_NV3,
        ["U_NV2"] = r.U_NV2,
        ["U_NV1"] = r.U_NV1,
        ["U_MARCA"] = r.U_MARCA,
        ["U_NOM_COLECCION"] = r.U_NOM_COLECCION,
        ["U_ATR_MED"] = r.U_ATR_MED,
        ["U_ATR_UNI"] = r.U_ATR_UNI,
        ["U_IXX_PACK"] = r.U_IXX_PACK,
        ["U_COD_PVR"] = r.U_COD_PVR,
        ["U_REPOSICION"] = r.U_REPOSICION,
        ["U_ORIGEN"] = r.U_ORIGEN,
        ["U_P_ORI"] = r.U_P_ORI,
        ["U_TIP_ABS"] = r.U_TIP_ABS,
        ["U_UNIDAD_DESPACHO"] = r.U_UNIDAD_DESPACHO,
        ["U_DESP_IN"] = r.U_DESP_IN,
        ["U_CANAL_VENTA"] = r.U_CANAL_VENTA,
        ["U_REGION"] = r.U_REGION,
        ["U_PR_ARQ"] = r.U_PR_ARQ,
        ["U_LIQ"] = r.U_LIQ,
        ["U_TIPO_BIEN"] = r.U_TIPO_BIEN
    };

    // InventoryUoMEntry is the numeric UoM entry id (e.g. -1); the human-readable UoM code
    // used across this API's "uomCode"/InventoryUoM fields is the string field InventoryUOM
    // (e.g. "UNIDAD") — confirmed against real Service Layer data.
    //
    // The field is BarCodeProperty (singular), not "CodeBars" — this SAP instance rejects
    // "CodeBars" with -1000 "Property 'CodeBars' of 'Item' is invalid" (verified live
    // 2026-08-07; the original 2026-08-06 fix never hit real SAP, only fixtures). The
    // external contract's JSON property stays "codeBars" (camelCase) — only the SAP
    // query field name was wrong, not this API's own response shape.
    private const string ItemSelectFields =
        "ItemCode,ItemName,InventoryUOM,QuantityOnStock,PurchaseItem,SalesItem,InventoryItem," +
        "Valid,Frozen,ItemType,ItemsGroupCode,Mainsupplier,BarCode";

    // PriceList 1 = "Precio Minorista" en SAP (convención de negocio fija, no configurable
    // por compañía). mba3-consultas valida el PVP del Excel contra este precio específico.
    private const int PrecioMinoristaPriceList = 1;

    public async Task<ItemResponse?> GetItemAsync(string itemCode, CancellationToken ct = default)
    {
        string json;
        var encoded = Uri.EscapeDataString(itemCode);
        try
        {
            try
            {
                json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                    $"Items('{encoded}')?$select={ItemSelectFields},U_IXX_PACK,U_MARCA", null, ct);
            }
            catch (SapException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                // Company DBs without the U_IXX_PACK/U_MARCA UDFs reject the $select; retry without them
                _logger.LogWarning(ex, "SAP rejected $select with U_IXX_PACK/U_MARCA for Item '{ItemCode}'. Retrying without the UDFs.", itemCode);
                json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                    $"Items('{encoded}')?$select={ItemSelectFields}", null, ct);
            }
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP Item '{ItemCode}' not found", itemCode); // NOSONAR
            return null;
        }

        var groups = await GetItemGroupsMapAsync(ct);
        var pvp = await GetPrecioMinoristaAsync(itemCode, ct);

        using var doc = JsonDocument.Parse(json);
        return ParseItem(doc.RootElement, itemCode, groups, pvp);
    }

    public async Task<IReadOnlyList<ItemResponse>> GetItemsByCodesAsync(
        IReadOnlyList<string> itemCodes, CancellationToken ct = default)
    {
        if (itemCodes.Count == 0)
        {
            return [];
        }

        var filter = string.Join(" or ", itemCodes.Select(c => $"ItemCode eq '{c.Replace("'", "''")}'"));
        // Unlike GetItemAsync, the batch also requests ItemWarehouseInfoCollection in the same call —
        // this is the one SAP round trip api-articulo-maestro's check_item_movements_in_sap needs
        // (item master data + per-warehouse stock together), instead of a second call per item.
        string json;
        try
        {
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items?$filter={filter}&$select={ItemSelectFields},U_IXX_PACK,U_MARCA,ItemWarehouseInfoCollection", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            _logger.LogWarning(ex, "SAP rejected $select with U_IXX_PACK/U_MARCA for items batch. Retrying without the UDFs.");
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items?$filter={filter}&$select={ItemSelectFields},ItemWarehouseInfoCollection", null, ct);
        }

        var groups = await GetItemGroupsMapAsync(ct);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(ValueProperty, out var arr))
        {
            return [];
        }

        var codes = new List<string>();
        var elements = new List<JsonElement>();
        foreach (var element in arr.EnumerateArray())
        {
            if (element.TryGetProperty(ItemCodeProperty, out var ic) && ic.ValueKind == JsonValueKind.String)
            {
                elements.Add(element);
                codes.Add(ic.GetString()!);
            }
        }

        // ItemPrices no es una navegación expandible en este SAP (SL la rechaza con
        // -1000 "Cannot expand invalid navigation property") — solo es accesible como
        // sub-recurso directo por ítem (Items('CODE')/ItemPrices), sin soporte de
        // $filter, así que no hay forma de traer el precio de varios ítems en una sola
        // llamada. Se resuelve con una llamada concurrente por ítem; SapThrottle (el
        // mismo semáforo que ya limita toda llamada a SAP vía ExecuteWithRetryOn401Async)
        // acota la concurrencia real a MaxConcurrentSapCalls, sin lógica nueva aquí.
        var precios = await Task.WhenAll(codes.Select(code => GetPrecioMinoristaAsync(code, ct)));

        var items = new List<ItemResponse>(elements.Count);
        for (var i = 0; i < elements.Count; i++)
        {
            items.Add(ParseItem(elements[i], codes[i], groups, precios[i]));
        }
        return items;
    }

    // Precio Minorista (PriceList 1) vía el sub-recurso Items('CODE')/ItemPrices.
    // null si el ítem no existe, no tiene esa lista de precio configurada, o la
    // llamada falla — nunca tumba la resolución del ítem por un dato opcional.
    private async Task<decimal?> GetPrecioMinoristaAsync(string itemCode, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(itemCode);
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, $"Items('{encoded}')/ItemPrices", null, ct);
            using var doc = JsonDocument.Parse(json);
            return ExtractPrecioMinorista(doc.RootElement);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is SapException or JsonException)
        {
            _logger.LogWarning(ex, "No se pudo obtener el Precio Minorista de '{ItemCode}'; se omite (pvp=null).", itemCode); // NOSONAR
            return null;
        }
    }

    private static ItemResponse ParseItem(
        JsonElement root, string fallbackItemCode, IReadOnlyDictionary<int, string> groups, decimal? pvp)
    {
        string? StrOpt(string field) =>
            root.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        // SL sometimes returns "" instead of null for unset master-data fields
        string? StrOptNormalized(string field) =>
            StrOpt(field) is { } s && !string.IsNullOrWhiteSpace(s) ? s : null;

        int? groupCode =
            root.TryGetProperty("ItemsGroupCode", out var gc) && gc.ValueKind == JsonValueKind.Number && gc.TryGetInt32(out var gcVal)
                ? gcVal
                : null;

        // Present only when the caller's $select included ItemWarehouseInfoCollection
        // (GetItemsByCodesAsync); absent for GetItemAsync, which leaves Warehouses null.
        var warehouses = root.TryGetProperty(ItemWarehouseInfoCollectionProperty, out _)
            ? ParseItemWarehouses(root)
            : null;

        return new ItemResponse(
            StrOpt(ItemCodeProperty) ?? fallbackItemCode,
            StrOpt(ItemNameProperty) ?? string.Empty,
            StrOpt(InventoryUomProperty),
            GetDecimalOrZero(root, "QuantityOnStock"),
            IsTyes(root, PurchaseItemProperty),
            IsTyes(root, "SalesItem"),
            IsTyes(root, "InventoryItem"),
            IsTyes(root, "Valid"),
            IsTyes(root, FrozenProperty),
            StrOpt("ItemType"),
            StrOptNormalized("U_IXX_PACK"),
            groupCode,
            groupCode is int g && groups.TryGetValue(g, out var groupName) ? groupName : null,
            StrOptNormalized("Mainsupplier"),
            pvp,
            StrOptNormalized(BarCodeProperty),
            StrOptNormalized("U_MARCA"),
            warehouses
        );
    }

    // Busca en la colección ItemPrices (respuesta del sub-recurso Items('CODE')/ItemPrices,
    // ver GetPrecioMinoristaAsync) la entrada de la lista de precio "Precio Minorista"
    // (PriceList 1) y devuelve su Price. null si el ítem no tiene esa lista de precio
    // configurada, o si ItemPrices no vino en la respuesta.
    private static decimal? ExtractPrecioMinorista(JsonElement root)
    {
        if (!root.TryGetProperty("ItemPrices", out var prices) || prices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var entry in prices.EnumerateArray())
        {
            if (entry.TryGetProperty("PriceList", out var pl) &&
                pl.ValueKind == JsonValueKind.Number &&
                pl.TryGetInt32(out var priceList) &&
                priceList == PrecioMinoristaPriceList &&
                entry.TryGetProperty("Price", out var price) &&
                price.ValueKind == JsonValueKind.Number &&
                price.TryGetDecimal(out var priceValue))
            {
                return priceValue;
            }
        }
        return null;
    }

    public async Task PrewarmItemGroupsAsync(CancellationToken ct = default)
    {
        // GetItemGroupsMapAsync never throws (it swallows load failures and leaves the cache
        // cold on error), so this is safe to fire at startup without guarding.
        var groups = await GetItemGroupsMapAsync(ct);
        _logger.LogInformation("ItemGroups cache pre-warmed with {Count} group(s)", groups.Count);
    }

    private async Task<IReadOnlyDictionary<int, string>> GetItemGroupsMapAsync(CancellationToken ct)
    {
        try
        {
            return await _itemGroupsCache.GetOrLoadAsync(FetchItemGroupsAsync, ct);
        }
        catch (Exception ex)
        {
            // Group-name enrichment must never fail an item lookup
            _logger.LogWarning(ex, "Could not load SAP ItemGroups; groupName will be null for this request");
            return new Dictionary<int, string>();
        }
    }

    private async Task<IReadOnlyDictionary<int, string>> FetchItemGroupsAsync(CancellationToken ct)
    {
        var map = new Dictionary<int, string>();
        string? resource = "ItemGroups?$select=Number,GroupName";

        // SL pages results (default 20 per page); follow odata.nextLink until exhausted
        const int maxPages = 100;
        for (var page = 0; resource is not null && page < maxPages; page++)
        {
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            AddItemGroupsFromPage(root, map);
            resource = GetNextLink(root);
        }

        _logger.LogInformation("Loaded {Count} SAP ItemGroups into cache", map.Count);
        return map;
    }

    private static void AddItemGroupsFromPage(JsonElement root, Dictionary<int, string> map)
    {
        if (!root.TryGetProperty(ValueProperty, out var arr))
        {
            return;
        }

        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.TryGetProperty("Number", out var num) && num.ValueKind == JsonValueKind.Number &&
                num.TryGetInt32(out var code) &&
                entry.TryGetProperty("GroupName", out var gn) && gn.ValueKind == JsonValueKind.String)
            {
                map[code] = gn.GetString()!;
            }
        }
    }

    // Shared odata.nextLink resolution for the two paginated SAP resources this gateway follows.
    private static string? GetNextLink(JsonElement root)
    {
        if (root.TryGetProperty("odata.nextLink", out var next) && next.ValueKind == JsonValueKind.String)
        {
            return next.GetString();
        }

        return root.TryGetProperty("@odata.nextLink", out var next4) && next4.ValueKind == JsonValueKind.String
            ? next4.GetString()
            : null;
    }

    public Task<ItemStockResponse?> GetItemStockAsync(string itemCode, string warehouseCode, CancellationToken ct = default)
        => GetItemStockInternalAsync(itemCode, warehouseCode, companyDb: null, password: null, ct);

    /// <summary>Same data as <see cref="GetItemStockAsync"/> but against Sap:TestCompanyDb — see interface doc.</summary>
    public Task<ItemStockResponse?> GetItemStockForTestCompanyAsync(string itemCode, string warehouseCode, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();
        return GetItemStockInternalAsync(itemCode, warehouseCode, _settings.TestCompanyDb, _settings.TestPassword, ct);
    }

    private async Task<ItemStockResponse?> GetItemStockInternalAsync(
        string itemCode, string warehouseCode, string? companyDb, string? password, CancellationToken ct)
    {
        string json;
        try
        {
            var encoded = Uri.EscapeDataString(itemCode);
            // Fetch the item with its per-warehouse collection and filter in-memory; // NOSONAR
            // SAP SL does not support $filter inside nested collections via OData.
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items('{encoded}')?$select=ItemCode,InventoryUOM,ItemWarehouseInfoCollection", null, companyDb, password, ct);
        }
        catch (SapException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP Item '{ItemCode}' not found when querying stock", itemCode); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty(ItemWarehouseInfoCollectionProperty, out var collection))
        {
            return null;
        }

        // Item-level UoM (same field GetItemsForValidationAsync exposes as SapItemValidationData.InventoryUoM)
        var uomCode = root.TryGetProperty(InventoryUomProperty, out var uom) && uom.ValueKind == JsonValueKind.String
            ? uom.GetString()
            : null;

        foreach (var entry in collection.EnumerateArray())
        {
            if (!entry.TryGetProperty(WarehouseCodeProperty, out var wc) || wc.GetString() != warehouseCode)
            {
                continue;
            }

            // Same resolution as GetItemsForValidationAsync's SapItemWarehouseData, so both
            // routes report identical InStock/Committed/Locked for the same item+warehouse.
            return new ItemStockResponse(
                itemCode, warehouseCode,
                GetDecimalOrZero(entry, "InStock"),
                GetDecimalOrZero(entry, "Committed"),
                uomCode,
                IsTyes(entry, "Locked"));
        }

        // Warehouse found in SAP catalog but no entry for this warehouse → stock 0
        return new ItemStockResponse(itemCode, warehouseCode, 0m, 0m, uomCode, Locked: false);
    }

    public async Task<IReadOnlyList<ItemSearchResult>> SearchItemsAsync(string query, int top, CancellationToken ct = default)
    {
        var escaped = query.Replace("'", "''");
        var resource = $"Items?$filter=contains(ItemCode,'{escaped}') or contains(BarCode,'{escaped}')" +
                       $"&$select=ItemCode,ItemName,BarCode,InventoryUOM&$top={top}";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty(ValueProperty, out var arr))
        {
            return [];
        }

        var results = new List<ItemSearchResult>();
        foreach (var item in arr.EnumerateArray())
        {
            if (!item.TryGetProperty(ItemCodeProperty, out var icEl) || icEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var itemName = item.TryGetProperty(ItemNameProperty, out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString()! : string.Empty;
            var barCode = item.TryGetProperty(BarCodeProperty, out var bcEl) && bcEl.ValueKind == JsonValueKind.String
                ? bcEl.GetString() : null;
            var uom = item.TryGetProperty("InventoryUOM", out var uomEl) && uomEl.ValueKind == JsonValueKind.String
                ? uomEl.GetString() : null;

            results.Add(new ItemSearchResult(icEl.GetString()!, itemName, barCode, uom));
        }

        return results;
    }

    public async Task<IReadOnlyList<ItemStockResponse>?> GetItemStockAllWarehousesAsync(string itemCode, CancellationToken ct = default)
    {
        string json;
        try
        {
            var encoded = Uri.EscapeDataString(itemCode);
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items('{encoded}')?$select=ItemCode,InventoryUOM,ItemWarehouseInfoCollection", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP Item '{ItemCode}' not found when querying stock (all warehouses)", itemCode); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty(ItemWarehouseInfoCollectionProperty, out var collection))
        {
            return [];
        }

        var uomCode = root.TryGetProperty("InventoryUOM", out var uom) && uom.ValueKind == JsonValueKind.String
            ? uom.GetString() : null;

        var result = new List<ItemStockResponse>();
        foreach (var entry in collection.EnumerateArray())
        {
            if (!entry.TryGetProperty(WarehouseCodeProperty, out var wc) || wc.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var inStock = GetDecimalOrZero(entry, "InStock");
            var committed = GetDecimalOrZero(entry, "Committed");
            if (inStock == 0 && committed == 0)
            {
                continue; // "mostrar por ubicación" solo tiene sentido donde hay algo
            }

            result.Add(new ItemStockResponse(itemCode, wc.GetString()!, inStock, committed, uomCode, IsTyes(entry, "Locked")));
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, WarehousePosInfo>> GetWarehousesPosInfoForTestCompanyAsync(
        IReadOnlyList<string> warehouseCodes, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();

        if (warehouseCodes.Count == 0)
        {
            return new Dictionary<string, WarehousePosInfo>();
        }

        var fullMap = await _warehousePosInfoCache.GetOrLoadAsync(
            _settings.TestCompanyDb!,
            loaderCt => LoadWarehousesPosInfoAsync(_settings.TestCompanyDb, _settings.TestPassword, loaderCt), ct);

        return warehouseCodes
            .Where(fullMap.ContainsKey)
            .ToDictionary(code => code, code => fullMap[code]);
    }

    /// <summary>Same POS flags but from the default company (Sap:CompanyDb) — see interface doc.</summary>
    public async Task<IReadOnlyDictionary<string, WarehousePosInfo>> GetWarehousesPosInfoAsync(
        IReadOnlyList<string> warehouseCodes, CancellationToken ct = default)
    {
        if (warehouseCodes.Count == 0)
        {
            return new Dictionary<string, WarehousePosInfo>();
        }

        // Cache keyed by company DB, so the default-company map never mixes with the test-company one.
        var fullMap = await _warehousePosInfoCache.GetOrLoadAsync(
            _settings.CompanyDb,
            loaderCt => LoadWarehousesPosInfoAsync(companyDb: null, password: null, loaderCt), ct);

        return warehouseCodes
            .Where(fullMap.ContainsKey)
            .ToDictionary(code => code, code => fullMap[code]);
    }

    private async Task<IReadOnlyDictionary<string, WarehousePosInfo>> LoadWarehousesPosInfoAsync(
        string? companyDb, string? password, CancellationToken ct)
    {
        var result = new Dictionary<string, WarehousePosInfo>();
        string? resource = "Warehouses?$select=WarehouseCode,U_WEBPOS,U_NEGOCIO";

        const int maxPages = 100;
        for (var page = 0; resource is not null && page < maxPages; page++)
        {
            var json = await ExecuteWithRetryOn401Async(
                HttpMethod.Get, resource, null, companyDb, password, ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            CollectWarehousePosInfoPage(root, result);
            resource = GetNextLink(root);
        }

        return result;
    }

    private static void CollectWarehousePosInfoPage(JsonElement root, Dictionary<string, WarehousePosInfo> result)
    {
        if (!root.TryGetProperty(ValueProperty, out var arr))
        {
            return;
        }

        foreach (var entry in arr.EnumerateArray())
        {
            var info = ParseWarehousePosInfo(entry);
            if (info is not null)
            {
                result[info.WarehouseCode] = info;
            }
        }
    }

    private static WarehousePosInfo? ParseWarehousePosInfo(JsonElement entry)
    {
        var code = StringOrNull(entry, WarehouseCodeProperty);
        if (code is null)
        {
            return null;
        }

        // U_WEBPOS is a plain alphanumeric UDF ('S'/other), not a SAP boolean field —
        // it must be compared as a string, unlike IsTyes()'s tYES/tNO encoding.
        var isPosEnabled = string.Equals(StringOrNull(entry, "U_WEBPOS"), "S", StringComparison.OrdinalIgnoreCase);

        var codBusiness = StringOrNull(entry, "U_NEGOCIO");
        if (string.IsNullOrWhiteSpace(codBusiness))
        {
            codBusiness = null;
        }

        return new WarehousePosInfo(code, isPosEnabled, codBusiness);
    }

    public async Task<StockTransferDocumentResponse?> GetStockTransferForTestCompanyAsync(int docEntry, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();

        string json;
        try
        {
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"StockTransfers({docEntry})?$select=DocEntry,DocNum,FromWarehouse,ToWarehouse,StockTransferLines",
                null, _settings.TestCompanyDb, _settings.TestPassword, ct);
        }
        catch (SapException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP StockTransfer DocEntry={DocEntry} not found (test company)", docEntry); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var fromWarehouse = StringOrEmpty(root, FromWarehouseProperty);
        var toWarehouse = StringOrEmpty(root, ToWarehouseProperty);

        return new StockTransferDocumentResponse(
            IntOrZero(root, DocEntryProperty), IntOrZero(root, DocNumProperty),
            fromWarehouse, toWarehouse, ParseStockTransferDocumentLines(root, toWarehouse));
    }

    // A line without its own WarehouseCode receives the document-level ToWarehouse.
    private static List<StockTransferDocumentLine> ParseStockTransferDocumentLines(JsonElement root, string toWarehouse)
    {
        var lines = new List<StockTransferDocumentLine>();
        if (!root.TryGetProperty(StockTransferLinesProperty, out var linesEl) || linesEl.ValueKind != JsonValueKind.Array)
        {
            return lines;
        }

        foreach (var line in linesEl.EnumerateArray())
        {
            var itemCode = StringOrNull(line, ItemCodeProperty);
            if (itemCode is null)
            {
                continue;
            }

            var warehouseCode = StringOrNull(line, WarehouseCodeProperty) ?? toWarehouse;
            lines.Add(new StockTransferDocumentLine(itemCode, DecimalOrZero(line, QuantityProperty), warehouseCode));
        }

        return lines;
    }

    public async Task<GoodsReceiptDocumentResponse?> GetGoodsReceiptAsync(int docEntry, CancellationToken ct = default)
    {
        string json;
        try
        {
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"PurchaseDeliveryNotes({docEntry})?$select=DocEntry,DocNum,DocumentLines",
                null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP PurchaseDeliveryNote DocEntry={DocEntry} not found", docEntry); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new GoodsReceiptDocumentResponse(
            IntOrZero(root, DocEntryProperty), IntOrZero(root, DocNumProperty), ParseGoodsReceiptDocumentLines(root));
    }

    // Lines without ItemCode or WarehouseCode are skipped: they cannot be mapped to a WEBPOS store stock.
    private static List<GoodsReceiptDocumentLine> ParseGoodsReceiptDocumentLines(JsonElement root)
    {
        var lines = new List<GoodsReceiptDocumentLine>();
        if (!root.TryGetProperty(DocumentLinesProperty, out var linesEl) || linesEl.ValueKind != JsonValueKind.Array)
        {
            return lines;
        }

        foreach (var line in linesEl.EnumerateArray())
        {
            var itemCode = StringOrNull(line, ItemCodeProperty);
            var warehouseCode = StringOrNull(line, WarehouseCodeProperty);
            if (itemCode is null || warehouseCode is null)
            {
                continue;
            }

            lines.Add(new GoodsReceiptDocumentLine(itemCode, DecimalOrZero(line, QuantityProperty), warehouseCode));
        }

        return lines;
    }

    /// <summary>
    /// Master-data fields WEBPOS needs to auto-create an item/store pair the first time it sees
    /// it ("El producto no existe."). Same source fields ProductosMabel already uses to create
    /// articles in HITELL: price list 1, U_IXX_MARCA, the tax code's U_X4R_INTERKEY, and every
    /// barcode on the item. Best-effort — a field SAP can't resolve comes back null rather than
    /// failing the whole lookup, since the auto-create retry is itself best-effort.
    /// </summary>
    public Task<ItemPosEnrichment?> GetItemPosEnrichmentForTestCompanyAsync(string itemCode, CancellationToken ct = default)
    {
        EnsureTestCredentialsConfigured();
        return GetItemPosEnrichmentInternalAsync(itemCode, _settings.TestCompanyDb, _settings.TestPassword, ct);
    }

    /// <summary>Same enrichment but from the default company (Sap:CompanyDb) — see interface doc.</summary>
    public Task<ItemPosEnrichment?> GetItemPosEnrichmentAsync(string itemCode, CancellationToken ct = default)
        => GetItemPosEnrichmentInternalAsync(itemCode, companyDb: null, password: null, ct);

    private async Task<ItemPosEnrichment?> GetItemPosEnrichmentInternalAsync(
        string itemCode, string? companyDb, string? password, CancellationToken ct)
    {
        string json;
        try
        {
            var encoded = Uri.EscapeDataString(itemCode);
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"Items('{encoded}')?$select=ItemCode,ArTaxCode,U_IXX_MARCA,ItemPrices,ItemBarCodeCollection",
                null, companyDb, password, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP Item '{ItemCode}' not found (company={CompanyDb}) when enriching for WEBPOS auto-create", itemCode, companyDb ?? _settings.CompanyDb); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var price = ExtractPriceList1(root);
        var brand = StringOrNull(root, "U_IXX_MARCA");

        string? codeTax = null;
        var arTaxCode = StringOrNull(root, "ArTaxCode");
        if (!string.IsNullOrWhiteSpace(arTaxCode))
        {
            codeTax = await TryGetTaxInterkeyAsync(arTaxCode, companyDb, password, ct);
        }

        return new ItemPosEnrichment(price, brand, codeTax, ExtractBarcodes(root));
    }

    /// <summary>Price of price list 1 as WEBPOS expects it (3 decimals, invariant culture), or null if the item has none.</summary>
    private static string? ExtractPriceList1(JsonElement root)
    {
        if (!root.TryGetProperty("ItemPrices", out var prices) || prices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var p in prices.EnumerateArray())
        {
            if (IsPriceList1(p) && p.TryGetProperty("Price", out var priceEl) && priceEl.TryGetDecimal(out var priceVal))
            {
                return Math.Round(priceVal, 3).ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    // SAP serializes PriceList either as a number or as a string depending on the SL version.
    private static bool IsPriceList1(JsonElement priceEntry)
    {
        if (!priceEntry.TryGetProperty("PriceList", out var pl))
        {
            return false;
        }

        return pl.ValueKind switch
        {
            JsonValueKind.String => pl.GetString() == "1",
            JsonValueKind.Number => pl.TryGetInt32(out var plNum) && plNum == 1,
            _ => false
        };
    }

    private static List<string> ExtractBarcodes(JsonElement root)
    {
        var barcodes = new List<string>();
        if (!root.TryGetProperty("ItemBarCodeCollection", out var bc) || bc.ValueKind != JsonValueKind.Array)
        {
            return barcodes;
        }

        foreach (var entry in bc.EnumerateArray())
        {
            var barcode = StringOrNull(entry, "Barcode");
            if (!string.IsNullOrWhiteSpace(barcode))
            {
                barcodes.Add(barcode);
            }
        }

        return barcodes;
    }

    private static string? StringOrNull(JsonElement element, string property) =>
        element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string StringOrEmpty(JsonElement element, string property) =>
        StringOrNull(element, property) ?? string.Empty;

    private static int IntOrZero(JsonElement element, string property) =>
        element.TryGetProperty(property, out var p) && p.TryGetInt32(out var v) ? v : 0;

    private static decimal DecimalOrZero(JsonElement element, string property) =>
        element.TryGetProperty(property, out var p) && p.TryGetDecimal(out var v) ? v : 0m;

    private async Task<string?> TryGetTaxInterkeyAsync(string taxCode, string? companyDb, string? password, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(taxCode);
            // No $select here: Service Layer answers "Property 'U_X4R_INTERKEY' of 'SalesTaxCodes' is
            // invalid" when a UDF of this entity is projected, even though the UDF comes back fine
            // on the full entity (confirmed live against TEST_MABEL, IVA_15 -> "3"). With the
            // $select every lookup failed and code_tax was always null on the WEBPOS auto-create.
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"SalesTaxCodes('{encoded}')",
                null, companyDb, password, ct);

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("U_X4R_INTERKEY", out var interkey) && interkey.ValueKind == JsonValueKind.String
                ? interkey.GetString()
                : null;
        }
        catch (SapException ex)
        {
            // Enrichment is best-effort — a missing/invalid tax code must not block the retry.
            _logger.LogWarning(ex, "Could not resolve U_X4R_INTERKEY for ArTaxCode='{TaxCode}' while enriching for WEBPOS auto-create", taxCode); // NOSONAR
            return null;
        }
    }
    // Usa la User Query STOCK_BODEGA (sml.svc) en vez de Items+ItemWarehouseInfoCollection:
    // esa colección no se puede filtrar por bodega en Service Layer 
    public async Task<(IReadOnlyList<ItemStockResponse> Items, IReadOnlyList<string> NotFound)> GetItemsStockBatchAsync(
        string warehouseCode, IReadOnlyList<string> itemCodes, CancellationToken ct = default)
    {
        if (itemCodes.Count == 0)
        {
            return ([], []);
        }

        var itemFilter = string.Join(" or ", itemCodes.Select(c => $"ItemCode eq '{c.Replace("'", "''")}'"));
        var escapedWarehouse = warehouseCode.Replace("'", "''");
        string? resource = $"sml.svc/STOCK_BODEGA?$filter=WhsCode eq '{escapedWarehouse}' and ({itemFilter})";

        var foundByCode = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        while (resource is not null)
        {
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            CollectStockBodegaPage(root, foundByCode);
            resource = GetNextStockBodegaPageResource(root);
        }

        return SplitFoundAndNotFound(warehouseCode, itemCodes, foundByCode);
    }

    private static void CollectStockBodegaPage(JsonElement root, Dictionary<string, decimal> foundByCode)
    {
        if (!root.TryGetProperty(ValueProperty, out var arr))
        {
            return;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty(ItemCodeProperty, out var ic) && ic.ValueKind == JsonValueKind.String &&
                item.TryGetProperty("OnHand", out var oh) && oh.TryGetDecimal(out var onHand))
            {
                foundByCode[ic.GetString()!] = onHand;
            }
        }
    }

    // SAP devuelve el nextLink relativo a sml.svc/ (ej. "STOCK_BODEGA?$skip=20"), sin el
    // prefijo — hay que reponerlo o la siguiente página resuelve "Service Not Found".
    private static string? GetNextStockBodegaPageResource(JsonElement root)
    {
        if (!root.TryGetProperty("@odata.nextLink", out var nextLink) || nextLink.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var next = nextLink.GetString()!;
        return next.StartsWith("sml.svc/", StringComparison.OrdinalIgnoreCase) ? next : $"sml.svc/{next}";
    }

    private static (IReadOnlyList<ItemStockResponse> Items, IReadOnlyList<string> NotFound) SplitFoundAndNotFound(
        string warehouseCode, IReadOnlyList<string> itemCodes, Dictionary<string, decimal> foundByCode)
    {
        var items = new List<ItemStockResponse>();
        var notFound = new List<string>();

        foreach (var itemCode in itemCodes)
        {
            if (foundByCode.TryGetValue(itemCode, out var onHand))
            {
                items.Add(new ItemStockResponse(itemCode, warehouseCode, onHand, 0m, null, false));
            }
            else
            {
                notFound.Add(itemCode);
            }
        }

        return (items, notFound);
    }


    public async Task<BusinessPartnerResponse?> GetBusinessPartnerAsync(string cardCode, CancellationToken ct = default)
    {
        string json;
        try
        {
            var encoded = Uri.EscapeDataString(cardCode);
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get,
                $"BusinessPartners('{encoded}')?$select=CardCode,CardName,CardType,Currency,Phone1,EmailAddress,Valid,Frozen", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP BusinessPartner '{CardCode}' not found", cardCode); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string Str(string field) =>
            root.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : string.Empty;
        string? StrOpt(string field) =>
            root.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        return new BusinessPartnerResponse(
            Str(CardCodeProperty),
            Str("CardName"),
            Str("CardType"),
            StrOpt("Currency"),
            StrOpt("Phone1"),
            StrOpt("EmailAddress"),
            Valid: IsTyes(root, "Valid"),
            Frozen: IsTyes(root, FrozenProperty)
        );
    }

    public async Task<InventoryTransferRequestsListResponse> GetInventoryTransferRequestsByCommentAsync( // NOSONAR
        string commentsContain, CancellationToken ct = default)
    {
        var escaped = commentsContain.Replace("'", "''");
        var resource = $"InventoryTransferRequests?$filter=contains(Comments,'{escaped}')&$select=DocEntry,DocNum,Comments,FromWarehouse,ToWarehouse,DocDate,DocumentStatus,StockTransferLines";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty(ValueProperty, out var arr))
        {
            return new InventoryTransferRequestsListResponse([]);
        }

        var items = arr.EnumerateArray().Select(item =>
        {
            int GetInt(string f) => item.TryGetProperty(f, out var p) && p.TryGetInt32(out var v) ? v : 0;
            string GetStr(string f) => item.TryGetProperty(f, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : string.Empty;
            string? GetStrOpt(string f) => item.TryGetProperty(f, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

            var lines = item.TryGetProperty(StockTransferLinesProperty, out var linesEl)
                ? linesEl.EnumerateArray().Select((l, i) => new InventoryTransferRequestLineResponse(
                    l.TryGetProperty("LineNum", out var ln) && ln.TryGetInt32(out var lv) ? lv : i,
                    l.TryGetProperty(ItemCodeProperty, out var li) && li.ValueKind == JsonValueKind.String ? li.GetString()! : string.Empty,
                    l.TryGetProperty(QuantityProperty, out var lq) && lq.TryGetDecimal(out var lqv) ? lqv : 0m,
                    l.TryGetProperty(WarehouseCodeProperty, out var lw) && lw.ValueKind == JsonValueKind.String ? lw.GetString() : null
                )).ToList()
                : (IReadOnlyList<InventoryTransferRequestLineResponse>)[];

            return new InventoryTransferRequestResponse(
                GetInt(DocEntryProperty),
                GetInt(DocNumProperty),
                GetStrOpt("Comments"),
                GetStr(FromWarehouseProperty),
                GetStr(ToWarehouseProperty),
                GetStr(DocDateProperty),
                GetStr("DocumentStatus"),
                lines
            );
        }).ToList();

        return new InventoryTransferRequestsListResponse(items);
    }

    public async Task UpdateInventoryTransferRequestStatusAsync(int docEntry, string status, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            U_STATUS = status // NOSONAR
        }, JsonOptions);

        try
        {
            await ExecuteWithRetryOn401Async(HttpMethod.Patch, $"InventoryTransferRequests({docEntry})", payload, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP InventoryTransferRequest DocEntry={DocEntry} not found (SAP 404)", docEntry); // NOSONAR
            throw;
        }
    }

    public async Task<InventoryTransferRequestsRawListResponse> GetInventoryTransferRequestsAsync(
        GetInventoryTransferRequestsRequest request, CancellationToken ct = default)
    {
        var resource = $"InventoryTransferRequests?$top={request.Top}&$skip={request.Skip}&$orderby=DocEntry desc";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        var items = node?[ValueProperty]?.AsArray()
            .OfType<System.Text.Json.Nodes.JsonObject>()
            .ToList()
            ?? [];

        return new InventoryTransferRequestsRawListResponse(items, items.Count);
    }

    public async Task<InventoryTransferRequestResponse?> GetInventoryTransferRequestAsync(int docEntry, CancellationToken ct = default)
    {
        string json;
        try
        {
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get, $"InventoryTransferRequests({docEntry})", null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("SAP InventoryTransferRequest DocEntry={DocEntry} not found (SAP 404)", docEntry); // NOSONAR
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int RequiredInt(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.TryGetInt32(out var v))
            {
                return v;
            }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de InventoryTransferRequest DocEntry={docEntry}");
        }
        string RequiredString(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String)
            {
                return p.GetString()!;
            }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de InventoryTransferRequest DocEntry={docEntry}");
        }
        decimal RequiredDecimal(JsonElement el, string field, string ctx)
        {
            if (el.TryGetProperty(field, out var p) && p.TryGetDecimal(out var v))
            {
                return v;
            }
            throw new SapResponseException($"SAP no devolvió el campo '{field}' en {ctx} de InventoryTransferRequest DocEntry={docEntry}");
        }
        string? OptString(JsonElement el, string field)
            => el.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        var lines = root.TryGetProperty(StockTransferLinesProperty, out var linesEl)
            ? linesEl.EnumerateArray()
                .Select((l, i) => new InventoryTransferRequestLineResponse(
                    RequiredInt(l, "LineNum", $"StockTransferLines[{i}]"),
                    RequiredString(l, ItemCodeProperty, $"StockTransferLines[{i}]"),
                    RequiredDecimal(l, QuantityProperty, $"StockTransferLines[{i}]"),
                    RequiredString(l, WarehouseCodeProperty, $"StockTransferLines[{i}]"),
                    // Confirmado en vivo contra TEST_MABEL: el campo real es "RemainingOpenQuantity"
                    // (mismo nombre que en PurchaseOrders) — no "OpenQuantity".
                    RequiredDecimal(l, "RemainingOpenQuantity", $"StockTransferLines[{i}]"),
                    OptString(l, "ItemDescription"),
                    OptString(l, "FromWarehouseCode")
                )).ToList()
            : (IReadOnlyList<InventoryTransferRequestLineResponse>)[];

        return new InventoryTransferRequestResponse(
            RequiredInt(root, DocEntryProperty, "root"),
            RequiredInt(root, DocNumProperty, "root"),
            OptString(root, "Comments"),
            RequiredString(root, FromWarehouseProperty, "root"),
            RequiredString(root, ToWarehouseProperty, "root"),
            RequiredString(root, DocDateProperty, "root"),
            RequiredString(root, "DocumentStatus", "root"),
            lines
        );
    }

    public async Task<IReadOnlyList<SapItemValidationData>> GetItemsForValidationAsync(
        IReadOnlyList<string> itemCodes, CancellationToken ct = default)
    {
        if (itemCodes.Count == 0)
        {
            return [];
        }

        var filter = string.Join(" or ", itemCodes.Select(c => $"ItemCode eq '{c.Replace("'", "''")}'"));
        var resource = $"Items?$filter={filter}" +
                       "&$select=ItemCode,ItemName,PurchaseItem,Valid,Frozen,InventoryUOM,ItemWarehouseInfoCollection";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(ValueProperty, out var arr))
        {
            return [];
        }

        var items = new List<SapItemValidationData>();
        foreach (var item in arr.EnumerateArray())
        {
            var parsed = ParseItemValidationData(item);
            if (parsed is not null)
            {
                items.Add(parsed);
            }
        }

        return items;
    }

    private static SapItemValidationData? ParseItemValidationData(JsonElement item)
    {
        if (!item.TryGetProperty(ItemCodeProperty, out var ic) || ic.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return new SapItemValidationData(
            ic.GetString()!,
            item.TryGetProperty(ItemNameProperty, out var iname) && iname.ValueKind == JsonValueKind.String ? iname.GetString()! : string.Empty,
            IsTyes(item, PurchaseItemProperty),
            IsTyes(item, "Valid"),
            IsTyes(item, FrozenProperty),
            item.TryGetProperty(InventoryUomProperty, out var uom) && uom.ValueKind == JsonValueKind.String ? uom.GetString() : null,
            ParseItemWarehouses(item)
        );
    }

    private static List<SapItemWarehouseData> ParseItemWarehouses(JsonElement item)
    {
        var warehouses = new List<SapItemWarehouseData>();
        if (!item.TryGetProperty(ItemWarehouseInfoCollectionProperty, out var collection) ||
            collection.ValueKind != JsonValueKind.Array)
        {
            return warehouses;
        }

        foreach (var entry in collection.EnumerateArray())
        {
            if (!entry.TryGetProperty(WarehouseCodeProperty, out var wc) || wc.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            // SL can return null for numeric fields; TryGetDecimal throws on non-Number kinds
            warehouses.Add(new SapItemWarehouseData(
                wc.GetString()!,
                GetDecimalOrZero(entry, "InStock"),
                GetDecimalOrZero(entry, "Committed"),
                IsTyes(entry, "Locked"),
                GetDecimalOrZero(entry, "Ordered")
            ));
        }

        return warehouses;
    }

    private Task<string> ExecuteWithRetryOn401Async(HttpMethod method, string resource, string? payload, CancellationToken ct)
        => ExecuteWithRetryOn401Async(method, resource, payload, companyDb: null, password: null, ct);

    private async Task<string> ExecuteWithRetryOn401Async(
        HttpMethod method, string resource, string? payload, string? companyDb, string? password, CancellationToken ct)
    {
        await _throttle.WaitAsync(ct);
        try
        {
            var token = companyDb is null
                ? await _sessionManager.GetSessionTokenAsync(ct)
                : await _sessionManager.GetSessionTokenAsync(companyDb, password!, ct);
            try
            {
                return await _client.SendAsync(method, resource, payload, token, ct);
            }
            catch (SapException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("SAP returned 401 for {Resource}. Invalidating session and retrying once.", resource); // NOSONAR
                if (companyDb is null)
                {
                    await _sessionManager.InvalidateSessionAsync(ct);
                }
                else
                {
                    await _sessionManager.InvalidateSessionAsync(companyDb, ct);
                }
                var newToken = companyDb is null
                    ? await _sessionManager.GetSessionTokenAsync(ct)
                    : await _sessionManager.GetSessionTokenAsync(companyDb, password!, ct);
                return await _client.SendAsync(method, resource, payload, newToken, ct);
            }
        }
        finally
        {
            _throttle.Release();
        }
    }


    public async Task<IReadOnlyList<WarehouseResponse>> GetWarehousesAsync(CancellationToken ct = default)
    {
        var warehouses = new List<WarehouseResponse>();
        string? resource = "Warehouses?$select=WarehouseCode,WarehouseName";

        // SL pages results (default 20 per page); follow odata.nextLink until exhausted
        const int maxPages = 100;
        for (var page = 0; resource is not null && page < maxPages; page++)
        {
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty(ValueProperty, out var arr))
            {
                warehouses.AddRange(ParseWarehouses(arr));
            }

            resource = GetNextLink(root);
        }

        return warehouses;
    }

    public async Task<IReadOnlyList<WarehouseResponse>> GetWarehousesByCodesAsync(
        IReadOnlyList<string> warehouseCodes, CancellationToken ct = default)
    {
        if (warehouseCodes.Count == 0)
        {
            return [];
        }

        var filter = string.Join(" or ", warehouseCodes.Select(c => $"WarehouseCode eq '{c.Replace("'", "''")}'"));
        var resource = $"Warehouses?$filter={filter}&$select=WarehouseCode,WarehouseName";

        var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(ValueProperty, out var arr)
            ? ParseWarehouses(arr)
            : [];
    }

    public async Task<IReadOnlyList<PriceListResponse>> GetPriceListsAsync(CancellationToken ct = default)
    {
        var priceLists = new List<PriceListResponse>();
        string? resource = "PriceLists?$select=PriceListNo,PriceListName";

        // SL pages results (default 20 per page); follow odata.nextLink until exhausted
        const int maxPages = 100;
        for (var page = 0; resource is not null && page < maxPages; page++)
        {
            var json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty(ValueProperty, out var arr))
            {
                foreach (var entry in arr.EnumerateArray())
                {
                    if (entry.TryGetProperty("PriceListNo", out var no) && no.TryGetInt32(out var noVal) &&
                        entry.TryGetProperty("PriceListName", out var name) && name.ValueKind == JsonValueKind.String)
                    {
                        priceLists.Add(new PriceListResponse(noVal, name.GetString()!));
                    }
                }
            }

            resource = GetNextLink(root);
        }

        return priceLists;
    }

    public async Task<bool> HasItemMovementsAsync(string itemCode, CancellationToken ct = default)
    {
        var resource = $"Items('{Uri.EscapeDataString(itemCode)}')?$select=" +
            "ItemCode,QuantityOnStock,QuantityOrderedFromVendors,QuantityOrderedByCustomers";

        string json;
        try
        {
            json = await ExecuteWithRetryOn401Async(HttpMethod.Get, resource, null, ct);
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "SAP Item '{ItemCode}' not found while checking movements; returning false",
                itemCode); // NOSONAR
            return false;
        }

        var item = JsonSerializer.Deserialize<SapItemMovementBalances>(json, JsonOptions)
            ?? throw new SapResponseException("SAP returned an empty Item payload while checking movements");

        return (item.QuantityOnStock ?? 0m) > 0m ||
               (item.QuantityOrderedFromVendors ?? 0m) > 0m ||
               (item.QuantityOrderedByCustomers ?? 0m) > 0m;
    }

    private static List<WarehouseResponse> ParseWarehouses(JsonElement arr)
    {
        var warehouses = new List<WarehouseResponse>();
        foreach (var entry in arr.EnumerateArray())
        {
            if (!entry.TryGetProperty(WarehouseCodeProperty, out var wc) || wc.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            warehouses.Add(new WarehouseResponse(
                wc.GetString()!,
                entry.TryGetProperty("WarehouseName", out var wn) && wn.ValueKind == JsonValueKind.String
                    ? wn.GetString()!
                    : string.Empty
            ));
        }
        return warehouses;
    }

    private static decimal GetDecimalOrZero(JsonElement el, string field) =>
        el.TryGetProperty(field, out var p) &&
        p.ValueKind == JsonValueKind.Number &&
        p.TryGetDecimal(out var v) ? v : 0m;

    // SAP Service Layer encodes boolean flags as "tYES"/"tNO" strings.
    // This helper normalises both that encoding and actual JSON booleans.
    private static bool IsTyes(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var p))
        {
            return false;
        }
        if (p.ValueKind == JsonValueKind.True)
        {
            return true;
        }
        if (p.ValueKind == JsonValueKind.False)
        {
            return false;
        }
        return p.ValueKind == JsonValueKind.String &&
               string.Equals(p.GetString(), "tYES", StringComparison.OrdinalIgnoreCase);
    }

    private static DocumentCreatedResult ParseDocumentResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty(DocEntryProperty, out var docEntryEl) || !docEntryEl.TryGetInt32(out var docEntry))
        {
            throw new SapResponseException("SAP no devolvió el campo 'DocEntry' en la respuesta de creación del documento");
        }

        if (!root.TryGetProperty(DocNumProperty, out var docNumEl) || !docNumEl.TryGetInt32(out var docNum))
        {
            throw new SapResponseException("SAP no devolvió el campo 'DocNum' en la respuesta de creación del documento");
        }

        return new DocumentCreatedResult(docEntry, docNum);
    }
}

file sealed record SapItemMovementBalances(
    string? ItemCode,
    decimal? QuantityOnStock,
    decimal? QuantityOrderedFromVendors,
    decimal? QuantityOrderedByCustomers);
