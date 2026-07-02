using Middleware.Domain.Enums;

namespace Middleware.Domain.Entities;

public sealed class StockTransfer
{
    public int DocEntry { get; private set; }
    public int DocNum { get; private set; }
    public string FromWarehouse { get; private set; } = string.Empty;
    public string ToWarehouse { get; private set; } = string.Empty;
    public DateOnly DocDate { get; private set; }
    public TransferType TransferType { get; private set; }
    public IReadOnlyList<StockTransferLine> Lines { get; private set; } = [];

    private StockTransfer() { }

    public static StockTransfer Create(
        string fromWarehouse,
        string toWarehouse,
        DateOnly docDate,
        TransferType transferType,
        IReadOnlyList<StockTransferLine> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromWarehouse);
        ArgumentException.ThrowIfNullOrWhiteSpace(toWarehouse);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one line is required.", nameof(lines));
        }

        return new StockTransfer
        {
            FromWarehouse = fromWarehouse,
            ToWarehouse = toWarehouse,
            DocDate = docDate,
            TransferType = transferType,
            Lines = lines
        };
    }

    public void SetDocumentNumbers(int docEntry, int docNum)
    {
        DocEntry = docEntry;
        DocNum = docNum;
    }
}

public sealed record StockTransferLine(
    string ItemCode,
    decimal Quantity,
    string? ProjectCode = null,
    string? FromBinCode = null,
    string? ToBinCode = null
);
