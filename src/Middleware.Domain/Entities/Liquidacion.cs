namespace Middleware.Domain.Entities;

public sealed class Liquidacion
{
    public int DocEntry { get; private set; }
    public int DocNum { get; private set; }
    public string CardCode { get; private set; } = string.Empty;
    public string? Comments { get; private set; }
    public IReadOnlyList<LiquidacionLine> Lines { get; private set; } = [];

    private Liquidacion() { }

    public static Liquidacion Create(
        string cardCode,
        IReadOnlyList<LiquidacionLine> lines,
        string? comments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardCode);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one line is required.", nameof(lines));
        }

        return new Liquidacion
        {
            CardCode = cardCode,
            Lines = lines,
            Comments = comments
        };
    }

    public void SetDocumentNumbers(int docEntry, int docNum)
    {
        DocEntry = docEntry;
        DocNum = docNum;
    }
}

public sealed record LiquidacionLine(
    string AccountCode,
    string ItemDescription,
    decimal Quantity,
    decimal UnitPrice,
    string TaxCode
);
