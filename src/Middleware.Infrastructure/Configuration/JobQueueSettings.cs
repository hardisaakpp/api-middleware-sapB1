namespace Middleware.Infrastructure.Configuration;

public sealed class JobQueueSettings
{
    public const string SectionName = "JobQueue";

    public int QueueAfterSeconds { get; init; } = 10;
    public int MaxRetryAttempts { get; init; } = 5;
    public int[] RetryBackoffSeconds { get; init; } = [15, 30, 60, 120, 240];
}
