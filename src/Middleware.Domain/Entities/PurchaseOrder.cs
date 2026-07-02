namespace Middleware.Domain.Entities;

public sealed class PurchaseOrder
{
    public int DocEntry { get; private set; }
    public int DocNum { get; private set; }
    public string VendorCode { get; private set; } = string.Empty;
    public DateOnly DocDate { get; private set; }
    public string DocStatus { get; private set; } = string.Empty;
    public IReadOnlyList<PurchaseOrderLine> Lines { get; private set; } = [];

    private PurchaseOrder() { }

    public static PurchaseOrder Create(
        int docEntry,
        int docNum,
        string vendorCode,
        DateOnly docDate,
        string docStatus,
        IReadOnlyList<PurchaseOrderLine> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorCode);

        return new PurchaseOrder
        {
            DocEntry = docEntry,
            DocNum = docNum,
            VendorCode = vendorCode,
            DocDate = docDate,
            DocStatus = docStatus,
            Lines = lines ?? []
        };
    }
}

public sealed record PurchaseOrderLine(
    string ItemCode,
    decimal Quantity,
    decimal OpenQuantity,
    decimal UnitPrice,
    string WarehouseCode
);
