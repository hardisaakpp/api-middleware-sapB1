namespace Middleware.Application.Dtos.Requests;

/// <summary>
/// Payload interno para notificar a WEBPOS el stock afectado por una entrada de mercadería
/// (PurchaseDeliveryNote) ya confirmada en SAP. A diferencia de las transferencias (from/to a
/// nivel documento), aquí cada línea trae su propia bodega receptora.
/// </summary>
public sealed record NotifyWebPosGoodsReceiptRequest(
    int DocEntry,
    int DocNum,
    IReadOnlyList<NotifyWebPosGoodsReceiptLine> Lines
);

public sealed record NotifyWebPosGoodsReceiptLine(string ItemCode, decimal Quantity, string WarehouseCode);
