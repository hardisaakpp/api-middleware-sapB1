using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

/// <summary>
/// Partial-update counterpart of <see cref="CreateItemRequest"/> — every field is optional since
/// SAP Service Layer PATCH only touches the properties present in the body. ItemCode is populated
/// by the controller from the route, mirroring UpdatePurchaseOrderRequest/DocEntry.
/// </summary>
public sealed record UpdateItemRequest(
    string ItemCode = "",
    string? ItemName = null,
    string? ForeignName = null,
    string? BarCode = null,
    string? InventoryUOM = null,
    string? PurchaseUnit = null,
    decimal? PurchaseItemsPerUnit = null,
    string? SalesUnit = null,
    decimal? SalesItemsPerUnit = null,
    int? ItemsGroupCode = null,
    string? ItemType = null,
    string? CostAccountingMethod = null,
    string? GLMethod = null,
    string? ManageStockByWarehouse = null,
    string? PurchaseItem = null,
    string? SalesItem = null,
    string? InventoryItem = null,
    string? VatLiable = null,
    string? WTLiable = null,
    string? IndirectTax = null,
    string? ArTaxCode = null,
    string? ApTaxCode = null,
    int? Manufacturer = null,
    int? ShipType = null,
    string? ManageBatchNumbers = null,
    string? PlanningSystem = null,
    string? ProcurementMethod = null,
    string? Frozen = null,
    string? Valid = null,
    string? ValidFrom = null,
    string? ValidTo = null,
    string? FrozenFrom = null,
    string? FrozenTo = null,
    string? Properties63 = null,
    int? LeadTime = null,
    decimal? MinOrderQuantity = null,
    string? U_ItemCode2 = null, // NOSONAR
    string? U_SXX_SKU_SIN_TALLA = null, // NOSONAR
    string? U_IXX_TALLA = null, // NOSONAR
    string? U_IXX_MARCA = null, // NOSONAR
    string? U_DESC_ECOMM = null, // NOSONAR
    string? U_GEN = null, // NOSONAR
    int? U_COD_COLOR = null, // NOSONAR
    string? U_IXX_COLORES = null, // NOSONAR
    int? U_COD_COL_SECU = null, // NOSONAR
    string? U_COLOR_SECU = null, // NOSONAR
    string? U_IXX_MATERIAL = null, // NOSONAR
    string? U_IXX_COMPOSICION = null, // NOSONAR
    string? U_NV3 = null, // NOSONAR
    string? U_NV2 = null, // NOSONAR
    string? U_NV1 = null, // NOSONAR
    string? U_MARCA = null, // NOSONAR
    string? U_NOM_COLECCION = null, // NOSONAR
    string? U_ATR_MED = null, // NOSONAR
    string? U_ATR_UNI = null, // NOSONAR
    string? U_IXX_PACK = null, // NOSONAR
    string? U_COD_PVR = null, // NOSONAR
    string? U_REPOSICION = null, // NOSONAR
    string? U_ORIGEN = null, // NOSONAR
    string? U_P_ORI = null, // NOSONAR
    string? U_TIP_ABS = null, // NOSONAR
    int? U_UNIDAD_DESPACHO = null, // NOSONAR
    int? U_DESP_IN = null, // NOSONAR
    string? U_CANAL_VENTA = null, // NOSONAR
    string? U_REGION = null, // NOSONAR
    decimal? U_PR_ARQ = null, // NOSONAR
    string? U_LIQ = null, // NOSONAR
    string? U_TIPO_BIEN = null, // NOSONAR
    IReadOnlyList<ItemPriceDto>? ItemPrices = null
);

public sealed class UpdateItemRequestValidator : AbstractValidator<UpdateItemRequest>
{
    public UpdateItemRequestValidator()
    {
        RuleFor(x => x.ItemCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.ItemName).MaximumLength(50);

        RuleFor(x => x.ManageStockByWarehouse).Must(SapItemEnums.BeValidYesNoOrNull).WithMessage(SapItemEnums.YesNoMessage);
        RuleFor(x => x.PurchaseItem).Must(SapItemEnums.BeValidYesNoOrNull).WithMessage(SapItemEnums.YesNoMessage);
        RuleFor(x => x.SalesItem).Must(SapItemEnums.BeValidYesNoOrNull).WithMessage(SapItemEnums.YesNoMessage);
        RuleFor(x => x.InventoryItem).Must(SapItemEnums.BeValidYesNoOrNull).WithMessage(SapItemEnums.YesNoMessage);
        RuleFor(x => x.VatLiable).Must(SapItemEnums.BeValidYesNoOrNull).WithMessage(SapItemEnums.YesNoMessage);
        RuleFor(x => x.WTLiable).Must(SapItemEnums.BeValidYesNoOrNull).WithMessage(SapItemEnums.YesNoMessage);
        RuleFor(x => x.IndirectTax).Must(SapItemEnums.BeValidYesNoOrNull).WithMessage(SapItemEnums.YesNoMessage);
        RuleFor(x => x.ManageBatchNumbers).Must(SapItemEnums.BeValidYesNoOrNull).WithMessage(SapItemEnums.YesNoMessage);
        RuleFor(x => x.Frozen).Must(SapItemEnums.BeValidYesNoOrNull).WithMessage(SapItemEnums.YesNoMessage);
        RuleFor(x => x.Valid).Must(SapItemEnums.BeValidYesNoOrNull).WithMessage(SapItemEnums.YesNoMessage);
        RuleFor(x => x.Properties63).Must(SapItemEnums.BeValidYesNoOrNull).WithMessage(SapItemEnums.YesNoMessage);

        RuleFor(x => x.ItemType).Must(SapItemEnums.BeValidItemTypeOrNull).WithMessage(SapItemEnums.ItemTypeMessage);
        RuleFor(x => x.PlanningSystem).Must(SapItemEnums.BeValidPlanningSystemOrNull).WithMessage(SapItemEnums.PlanningSystemMessage);
        RuleFor(x => x.ProcurementMethod).Must(SapItemEnums.BeValidProcurementMethodOrNull).WithMessage(SapItemEnums.ProcurementMethodMessage);
        RuleFor(x => x.GLMethod).Must(SapItemEnums.BeValidGLMethodOrNull).WithMessage(SapItemEnums.GLMethodMessage);

        RuleForEach(x => x.ItemPrices).ChildRules(price =>
        {
            price.RuleFor(p => p.PriceList).GreaterThan(0);
            price.RuleFor(p => p.Price).GreaterThanOrEqualTo(0);
        });
    }
}
