using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record GetItemsBatchRequest(
    IReadOnlyList<string> ItemCodes
);

public sealed class GetItemsBatchRequestValidator : AbstractValidator<GetItemsBatchRequest>
{
    public const int MaxCodes = 100;

    public GetItemsBatchRequestValidator()
    {
        // An empty list is valid (returns empty results); a missing/null list is not
        RuleFor(x => x.ItemCodes).NotNull().WithMessage("itemCodes is required.");
        RuleFor(x => x.ItemCodes)
            .Must(c => c is null || c.Count <= MaxCodes)
            .WithMessage($"A maximum of {MaxCodes} item codes is allowed per batch.");
        RuleForEach(x => x.ItemCodes).NotEmpty().MaximumLength(50);
    }
}
