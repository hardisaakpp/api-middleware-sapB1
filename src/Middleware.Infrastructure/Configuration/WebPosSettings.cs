namespace Middleware.Infrastructure.Configuration;

public sealed class WebPosSettings
{
    public const string SectionName = "WebPos";

    public string BaseUrl { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxConcurrentCalls { get; init; } = 5;
    public int ThrottleWaitSeconds { get; init; } = 10;
    public int WarehousePosInfoCacheMinutes { get; init; } = 360;
}
