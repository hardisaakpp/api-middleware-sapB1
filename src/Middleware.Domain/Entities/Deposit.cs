using Middleware.Domain.Enums;

namespace Middleware.Domain.Entities;

public sealed class Deposit
{
    public int DocEntry { get; private set; }
    public int DocNum { get; private set; }
    public DepositType Type { get; private set; }
    public CompanyEnvironment Environment { get; private set; }
    public DateOnly DocDate { get; private set; }
    public string? Comments { get; private set; }
    public IReadOnlyList<DepositLine> Lines { get; private set; } = [];

    private Deposit() { }

    public static Deposit Create(
        DepositType type,
        CompanyEnvironment environment,
        DateOnly docDate,
        IReadOnlyList<DepositLine> lines,
        string? comments = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one line is required.", nameof(lines));
        }

        return new Deposit
        {
            Type = type,
            Environment = environment,
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

public sealed record DepositLine(
    string ItemCode,
    decimal Quantity,
    string WarehouseCode,
    string? AccountCode = null
);
