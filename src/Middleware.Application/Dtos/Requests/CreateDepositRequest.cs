using FluentValidation;
using Middleware.Domain.Enums;

namespace Middleware.Application.Dtos.Requests;

public sealed record CreateDepositRequest(
    DepositType Type,
    CompanyEnvironment Environment,
    DateOnly DocDate,
    string? Comments,
    IReadOnlyList<DepositLineDto> Lines
);

public sealed record DepositLineDto(
    string ItemCode,
    decimal Quantity,
    string WarehouseCode
);

public sealed class CreateDepositRequestValidator : AbstractValidator<CreateDepositRequest>
{
    public CreateDepositRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Environment).IsInEnum();
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemCode).NotEmpty().MaximumLength(20);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.WarehouseCode).NotEmpty().MaximumLength(8);
        });
    }
}
