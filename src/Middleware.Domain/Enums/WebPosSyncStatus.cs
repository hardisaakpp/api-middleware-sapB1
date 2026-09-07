namespace Middleware.Domain.Enums;

public enum WebPosSyncStatus
{
    Synced,
    SkippedNotPos,
    Rejected,
    Queued,
    Error
}
