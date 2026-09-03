namespace Middleware.Application.Abstractions;

public interface IWebPosGateway
{
    /// <summary>
    /// Sets the absolute post-transfer stock (overwrite_stock=true) for one item across one or
    /// more stores that all belong to the same WEBPOS "negocio" (codBusiness).
    /// Business rejections (WEBPOS answers with <c>procesoCorrecto:false</c>, on any HTTP status)
    /// come back as <see cref="WebPosUpdateOutcome.Success"/> = false — they are never thrown as
    /// exceptions and never retried automatically. Only genuine availability failures (timeout,
    /// 5xx, connection errors) are thrown, so the job queue can retry those.
    /// </summary>
    Task<WebPosUpdateOutcome> UpdateInventoryAsync(
        string codBusiness,
        string itemCode,
        IReadOnlyList<WebPosStoreStock> storeStock,
        CancellationToken ct = default);
}

/// <summary>
/// Price/Brand/CodeTax/Barcodes are only populated on the auto-create retry, when WEBPOS answers
/// "El producto no existe." — a plain stock update never sends them (they're omitted from the
/// wire payload when null, keeping the normal-path request minimal).
/// </summary>
public sealed record WebPosStoreStock(
    string SapStoreCode,
    decimal Stock,
    bool OverwriteStock = true,
    string? Price = null,
    string? Brand = null,
    string? CodeTax = null,
    IReadOnlyList<string>? Barcodes = null);

public sealed record WebPosUpdateOutcome(bool Success, string? RejectionReason = null);
