using Middleware.Domain.Enums;

namespace Middleware.Application.Dtos.Responses;

public sealed record WebPosSyncInfo(
    WebPosSyncStatus Status,
    string? Reason = null,
    Guid? JobId = null
);
