using Middleware.Api;
using Middleware.Api.HealthChecks;
using Middleware.Api.Logging;
using Middleware.Application;
using Middleware.Application.UseCases.Deposits;
using Middleware.Application.UseCases.Validations;
using Middleware.Infrastructure;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Local secrets layer, on top of appsettings.{Environment}.json — see docs/configuration.md
    builder.Configuration.AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.local.json",
        optional: true,
        reloadOnChange: true);

    StartupConfiguration.ConfigureLogging(builder);
    StartupConfiguration.ConfigureApiVersioning(builder);
    StartupConfiguration.ConfigureControllers(builder);
    StartupConfiguration.ConfigureSwagger(builder);

    // Health checks
    builder.Services.AddHealthChecks()
        .AddCheck<SapHealthCheck>("sap", tags: ["ready"]);

    // Application & Infrastructure layers
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // Audit logging persisted off the request critical path via a background queue
    builder.Services.AddSingleton<ApiLogQueue>();
    builder.Services.AddHostedService<ApiLogBackgroundService>();

    // Deposit environment settings (Production vs Test CompanyDB)
    builder.Services.Configure<DepositSettings>(
        builder.Configuration.GetSection(DepositSettings.SectionName));

    // Batch validation settings (timeout, SL chunk size, parallelism)
    builder.Services.Configure<ValidationSettings>(
        builder.Configuration.GetSection(ValidationSettings.SectionName));

    var app = builder.Build();

    StartupConfiguration.ConfigureRequestPipeline(app);

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application startup failed");
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Exposes the implicit Program class for WebApplicationFactory in integration tests
public partial class Program { } // NOSONAR
