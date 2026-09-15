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

    /// <summary>
    /// Root of the WEBPOS server with a trailing slash. The client appends its own paths
    /// (<c>apilogin/external</c>, <c>external/inventory/{business}</c>), so a BaseUrl that already
    /// ends in <c>/external</c> — as production was configured on 2026-09-09 — produces
    /// <c>/external/apilogin/external</c> and WEBPOS answers every call with "token inválido".
    /// That suffix is stripped here so a copy-pasted URL cannot silently break the integration.
    /// </summary>
    public string ResolvedBaseUrl => NormalizeBaseUrl(BaseUrl).Url;

    public static (string Url, bool StrippedExternal) NormalizeBaseUrl(string? baseUrl)
    {
        var url = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        var stripped = false;
        while (url.EndsWith("/external", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^"/external".Length].TrimEnd('/');
            stripped = true;
        }

        return (url + "/", stripped);
    }
}
