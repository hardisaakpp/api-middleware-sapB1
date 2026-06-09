namespace Middleware.Domain.Entities;

public sealed class Liquidacion
{
    public int DocEntry { get; private set; }
    public int DocNum { get; private set; }
    public DateOnly DocDate { get; private set; }
    public string? Comments { get; private set; }
    public IReadOnlyList<LiquidacionLine> Lines { get; private set; } = [];

    private Liquidacion() { }

    public static Liquidacion Create(
        DateOnly docDate,
        IReadOnlyList<LiquidacionLine> lines,
        string? comments = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one line is required.", nameof(lines));
        }

        return new Liquidacion
        {
            DocDate = docDate,
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
    string ItemCode,
    decimal Quantity,
    string WarehouseCode,
    string? AccountCode = null
);
