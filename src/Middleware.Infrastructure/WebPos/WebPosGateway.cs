using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;

namespace Middleware.Infrastructure.WebPos;

internal sealed class WebPosGateway : IWebPosGateway
{
    private readonly IWebPosSessionManager _sessionManager;
    private readonly IWebPosClient _client;
    private readonly WebPosThrottle _throttle;
    private readonly ILogger<WebPosGateway> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WebPosGateway(IWebPosSessionManager sessionManager, IWebPosClient client, WebPosThrottle throttle, ILogger<WebPosGateway> logger)
    {
        _sessionManager = sessionManager;
        _client = client;
        _throttle = throttle;
        _logger = logger;
    }

    public async Task<WebPosUpdateOutcome> UpdateInventoryAsync(
        string codBusiness, string itemCode, IReadOnlyList<WebPosStoreStock> storeStock, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            cd_itm = itemCode,
            store_stock = storeStock.Select(s => new
            {
                sap_store_code = s.SapStoreCode,
                // HITELL deserializes `stock` into a java.lang.Integer: a decimal like 5806.0 is rejected
                // ("Error deserialize JSON value into type: class java.lang.Integer"), so the SAP
                // decimal is sent as a whole number, same as the legacy ProductosMabel service does.
                stock = ToWholeStock(s.Stock),
                overwrite_stock = s.OverwriteStock,
                // Only populated on the auto-create retry (WEBPOS answered "El producto no
                // existe.") — omitted from the wire payload on a plain stock update.
                price = s.Price,
                brand = s.Brand,
                code_tax = s.CodeTax,
                cod_pos_barcode = s.Barcodes is { Count: > 0 } ? s.Barcodes : null
            })
        }, JsonOptions);

        var resource = $"external/inventory/{Uri.EscapeDataString(codBusiness)}";
        var json = await ExecuteWithRetryOn401Async(HttpMethod.Post, resource, payload, ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Success is decided solely by `procesoCorrecto` — WEBPOS uses both 200 and 400 to carry
        // business rejections, so the transport status code is not a reliable signal here.
        if (root.TryGetProperty("procesoCorrecto", out var pc) && pc.ValueKind == JsonValueKind.True)
        {
            return new WebPosUpdateOutcome(true);
        }

        var mensaje = root.TryGetProperty("mensaje", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()
            : "WEBPOS rechazó la actualización sin detalle.";

        _logger.LogWarning(
            "WEBPOS rechazó la actualización de inventario — codBusiness={CodBusiness} cd_itm={ItemCode} mensaje={Mensaje}",
            codBusiness, itemCode, mensaje);

        return new WebPosUpdateOutcome(false, mensaje);
    }

    // SAP stock for POS items is whole units; rounding (not truncating) keeps 4.9999 = 5.
    private static long ToWholeStock(decimal stock) =>
        decimal.ToInt64(Math.Round(stock, 0, MidpointRounding.AwayFromZero));

    private async Task<string> ExecuteWithRetryOn401Async(HttpMethod method, string resource, string payload, CancellationToken ct)
    {
        await _throttle.WaitAsync(ct);
        try
        {
            var token = await _sessionManager.GetTokenAsync(ct);
            try
            {
                return await _client.SendAsync(method, resource, payload, token, ct);
            }
            catch (WebPosException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("WEBPOS returned 401 for {Resource}. Invalidating session and retrying once.", resource); // NOSONAR
                await _sessionManager.InvalidateAsync(ct);
                var newToken = await _sessionManager.GetTokenAsync(ct);
                return await _client.SendAsync(method, resource, payload, newToken, ct);
            }
        }
        finally
        {
            _throttle.Release();
        }
    }
}
