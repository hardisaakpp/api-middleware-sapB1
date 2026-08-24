using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

/// <summary>
/// Mirrors the SAP Items resource shape that api-articulo-maestro's sap_sync.py::_prepare_sap_json
/// already builds today: field names and enum-like values (e.g. "tYES"/"tNO", "itItems") are the
/// SAP-facing ones. The Y/N-to-tYES/tNO and similar mappings are the caller's responsibility
/// (they already own that logic); this middleware forwards the payload, it does not re-map it.
/// </summary>
public sealed record CreateItemRequest(
    string ItemCode,
    string ItemName,
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

public sealed record ItemPriceDto(int PriceList, decimal Price, string Currency = "USD");

public sealed class CreateItemRequestValidator : AbstractValidator<CreateItemRequest>
{
    public CreateItemRequestValidator()
    {
        RuleFor(x => x.ItemCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.ItemName).NotEmpty().MaximumLength(50);

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

/// <summary>SAP Item enum-like literal sets shared by Create/UpdateItemRequestValidator — kept in one place so both stay in sync.</summary>
internal static class SapItemEnums
{
    public const string YesNoMessage = "must be 'tYES', 'tNO' or omitted.";
    public const string ItemTypeMessage = "must be 'itItems', 'itLabor', 'itTravel' or omitted.";
    public const string PlanningSystemMessage = "must be 'bop_MRP', 'bop_None' or omitted.";
    public const string ProcurementMethodMessage = "must be 'bom_Buy', 'bom_Make' or omitted.";
    public const string GLMethodMessage = "must be 'glm_ItemClass', 'glm_WH', 'glm_ItemLevel' or omitted.";

    public static bool BeValidYesNoOrNull(string? value) => value is null or "tYES" or "tNO";
    public static bool BeValidItemTypeOrNull(string? value) => value is null or "itItems" or "itLabor" or "itTravel";
    public static bool BeValidPlanningSystemOrNull(string? value) => value is null or "bop_MRP" or "bop_None";
    public static bool BeValidProcurementMethodOrNull(string? value) => value is null or "bom_Buy" or "bom_Make";
    public static bool BeValidGLMethodOrNull(string? value) => value is null or "glm_ItemClass" or "glm_WH" or "glm_ItemLevel";
}
