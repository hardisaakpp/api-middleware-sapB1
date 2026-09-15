using System.Text.RegularExpressions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.WebPos;

/// <summary>
/// Turns the raw HITELL rejection text persisted in pos_sync_status.reason into something the
/// client (Nexus) can act on without parsing HITELL messages itself. The reason is written by
/// the Notify use cases as "ITEMCODE (codBusiness): mensaje" joined by " | ", one entry per
/// (item, business) pair HITELL rejected.
/// </summary>
public static partial class WebPosRejectionClassifier
{
    /// <summary>
    /// HITELL answers "item does not exist in this business" with two different texts depending
    /// on the code path: the inventory endpoint's business message and a raw JPA error. Both mean
    /// the product has to be created in HITELL before the stock can be pushed.
    /// </summary>
    private static readonly string[] ProductNotFoundMessages =
    [
        "El producto no existe.",
        "No entity found for query"
    ];

    [GeneratedRegex(@"^(?<item>\S+) \((?<biz>[^)]*)\): (?<msg>.*)$", RegexOptions.Singleline)]
    private static partial Regex ItemReasonRegex();

    public static bool IsProductNotFound(string? message) =>
        message is not null &&
        Array.Exists(ProductNotFoundMessages, m => m.Equals(message.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string ClassifyMessage(string? message) =>
        IsProductNotFound(message) ? WebPosRejectionTypes.ProductNotFound : WebPosRejectionTypes.Other;

    /// <summary>Splits a persisted reason into its per-item rejections; a reason that doesn't follow the pattern becomes one item-less entry.</summary>
    public static IReadOnlyList<WebPosRejectedItem> ParseItems(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return [];
        }

        return reason
            .Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseItem)
            .ToList();
    }

    private static WebPosRejectedItem ParseItem(string part)
    {
        var match = ItemReasonRegex().Match(part);
        if (!match.Success)
        {
            return new WebPosRejectedItem(null, null, part, ClassifyMessage(part));
        }

        var message = match.Groups["msg"].Value.Trim();
        return new WebPosRejectedItem(match.Groups["item"].Value, match.Groups["biz"].Value, message, ClassifyMessage(message));
    }

    /// <summary>Overall type of a document rejection: PRODUCTO_NO_EXISTE only when every rejected item is a missing product.</summary>
    public static string ClassifyReason(string? reason)
    {
        var items = ParseItems(reason);
        return items.Count > 0 && items.All(i => i.Type == WebPosRejectionTypes.ProductNotFound)
            ? WebPosRejectionTypes.ProductNotFound
            : WebPosRejectionTypes.Other;
    }
}
