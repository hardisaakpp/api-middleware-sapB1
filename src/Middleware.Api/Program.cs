using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Middleware.Api.HealthChecks;
using Middleware.Api.Middleware;
using Middleware.Application;
using Middleware.Application.UseCases.Deposits;
using Middleware.Infrastructure;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using System.Text.Json;
using System.Text.Json.Serialization;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Structured logging with Serilog
    builder.Host.UseSerilog((ctx, services, config) =>
    {
        var isDev = ctx.HostingEnvironment.IsDevelopment();
        var formatter = new CompactJsonFormatter();

        config
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "Middleware.Api")
            .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
            .WriteTo.File(
                formatter,
                path: "logs/middleware-.json",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: isDev ? 7 : 30,
                shared: true);

        if (isDev)
        {
            config.WriteTo.Console(formatter);
        }
    });

    // API versioning
    builder.Services.AddApiVersioning(opt =>
    {
        opt.DefaultApiVersion = new ApiVersion(1, 0);
        opt.AssumeDefaultVersionWhenUnspecified = true;
        opt.ReportApiVersions = true;
    }).AddApiExplorer(opt =>
    {
        opt.GroupNameFormat = "'v'VVV";
        opt.SubstituteApiVersionInUrl = true;
    });

    // Controllers with JSON options
    builder.Services.AddControllers()
        .AddJsonOptions(opt =>
        {
            opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            opt.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            opt.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

    // Swagger (dev only — avoids assembly scanning overhead in production)
    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(opt =>
        {
            opt.SwaggerDoc("v1", new() { Title = "SAP Middleware API", Version = "v1" });
        });
    }

    // Health checks
    builder.Services.AddHealthChecks()
        .AddCheck<SapHealthCheck>("sap", tags: ["ready"]);

    // Application & Infrastructure layers
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // Deposit environment settings (Production vs Test CompanyDB)
    builder.Services.Configure<DepositSettings>(
        builder.Configuration.GetSection(DepositSettings.SectionName));

    var app = builder.Build();

    // Pipeline order matters: correlation ID must run first so all subsequent middleware has the ID
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ApiLogMiddleware>();
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0}ms";
        opts.GetLevel = (ctx, _, ex) =>
            ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error :
            ctx.Response.StatusCode >= 400 ? LogEventLevel.Warning :
            LogEventLevel.Information;
        opts.EnrichDiagnosticContext = (diag, ctx) =>
        {
            if (ctx.Items.TryGetValue("X-Correlation-Id", out var id))
            {
                diag.Set("CorrelationId", id?.ToString());
            }
        };
    });
    app.UseMiddleware<GlobalExceptionMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(opt => opt.SwaggerEndpoint("/swagger/v1/swagger.json", "SAP Middleware API v1"));
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }
    app.UseAuthorization();
    app.MapControllers();

    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application startup failed");
}
finally
{
    Log.CloseAndFlush();
}

// Exposes the implicit Program class for WebApplicationFactory in integration tests
public partial class Program { }
