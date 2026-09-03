using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

/// <summary>
/// Listado paginado de InventoryTransferRequests (SOT), más nuevo primero — mismo patrón
/// que GetPurchaseOrdersRequest. Antes de esto el único endpoint de listado era
/// commentsContain (búsqueda por texto en el comentario), sin paginación ni forma de
/// traer "todas las abiertas" — gap encontrado al construir P-008 de api-cedi.
/// </summary>
public sealed record GetInventoryTransferRequestsRequest(int Top = 20, int Skip = 0);

public sealed class GetInventoryTransferRequestsRequestValidator : AbstractValidator<GetInventoryTransferRequestsRequest>
{
    public GetInventoryTransferRequestsRequestValidator()
    {
        RuleFor(x => x.Top).InclusiveBetween(1, 100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
    }
}
