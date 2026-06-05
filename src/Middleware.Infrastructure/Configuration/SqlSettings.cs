namespace Middleware.Infrastructure.Configuration;

public sealed class SqlSettings
{
    public const string SectionName = "Sql";

    public string ConnectionString { get; init; } = string.Empty;
}
