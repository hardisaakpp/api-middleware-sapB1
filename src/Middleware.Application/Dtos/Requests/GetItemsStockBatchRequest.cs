using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record GetItemsStockBatchRequest(
    string WarehouseCode,
    IReadOnlyList<string> ItemCodes
);

public sealed class GetItemsStockBatchRequestValidator: AbstractValidator<GetItemsStockBatchRequest>
{
    public const int MaxCodes = 100;

    public GetItemsStockBatchRequestValidator()
    {
        RuleFor(x => x.WarehouseCode).NotEmpty().MaximumLength(8);
        RuleFor(x => x.ItemCodes).NotNull().WithMessage("itemCodes is required.");
        RuleFor(x => x.ItemCodes)
        .Must(c => c is null || c.Count <= MaxCodes)
        .WithMessage($"A maximum of {MaxCodes} item codes is allowed per batch.");
        RuleForEach(x => x.ItemCodes).NotEmpty().MaximumLength(50);
    }
}