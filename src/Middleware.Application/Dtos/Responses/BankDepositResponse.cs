namespace Middleware.Application.Dtos.Responses;

/// <summary>
/// Property names match Tesoreria.Application.Depositos.Dtos.SapDepositResponseDto
/// (the Refit client contract in api-tesoreria) exactly — DocEntry/Mensaje.
/// </summary>
public sealed record BankDepositResponse(int DocEntry, string? Mensaje);
