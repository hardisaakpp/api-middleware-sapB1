namespace Middleware.Api.Configuration;

public sealed class ApiKeySettings
{
    public const string SectionName = "ApiKey";

    public string HeaderName { get; init; } = "X-Api-Key";

    /// <summary>Shared secret expected on <see cref="HeaderName"/>. Empty in Development disables enforcement; empty elsewhere fails closed.</summary>
    public string? Value { get; init; }
}
