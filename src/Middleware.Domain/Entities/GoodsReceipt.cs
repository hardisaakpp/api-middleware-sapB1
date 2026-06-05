namespace Middleware.Domain.Entities;

public sealed class GoodsReceipt
{
    public int DocEntry { get; private set; }
    public int DocNum { get; private set; }
    public string VendorCode { get; private set; } = string.Empty;
    public DateOnly DocDate { get; private set; }
    public string? Comments { get; private set; }
    public IReadOnlyList<GoodsReceiptLine> Lines { get; private set; } = [];

    private GoodsReceipt() { }

    public static GoodsReceipt Create(
        string vendorCode,
        DateOnly docDate,
        IReadOnlyList<GoodsReceiptLine> lines,
        string? comments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorCode);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0) throw new ArgumentException("At least one line is required.", nameof(lines));

        return new GoodsReceipt
        {
            VendorCode = vendorCode,
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

public sealed record GoodsReceiptLine(
    string ItemCode,
    decimal Quantity,
    decimal UnitPrice,
    string WarehouseCode
);
