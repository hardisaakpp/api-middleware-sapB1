using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Middleware.Api.Middleware;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Middleware.Api;

internal static class StartupConfiguration
{
    public static void ConfigureLogging(WebApplicationBuilder builder)
    {
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
                // Console always: in the container this is what `docker logs` shows. Until 2026-09-09
                // production wrote only to logs/ (unwritable for the non-root app user), so the
                // HITELL incident left no trace at all.
                .WriteTo.Console(formatter)
                .WriteTo.File(
                    formatter,
                    path: "logs/middleware-.json",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: isDev ? 7 : 30,
                    shared: true);
        });
    }

    public static void ConfigureApiVersioning(WebApplicationBuilder builder)
    {
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
    }

    public static void ConfigureControllers(WebApplicationBuilder builder)
    {
        builder.Services.AddControllers()
            .AddJsonOptions(opt =>
            {
                opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                opt.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                opt.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });
    }

    // Swagger (dev and test only — avoids assembly scanning overhead in production)
    public static void ConfigureSwagger(WebApplicationBuilder builder)
    {
        if (!IsSwaggerEnabled(builder.Environment, builder.Configuration))
        {
            return;
        }

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(opt =>
        {
            opt.SwaggerDoc("v1", new() { Title = "SAP Middleware API", Version = "v1" });
        });
    }

    public static void ConfigureRequestPipeline(WebApplication app)
    {
        // Pipeline order matters: correlation ID must run first so all subsequent middleware has the ID
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ApiLogMiddleware>();
        app.UseSerilogRequestLogging(opts =>
        {
            opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0}ms";
            opts.GetLevel = (ctx, _, ex) => GetLogEventLevel(ctx, ex);
            opts.EnrichDiagnosticContext = EnrichDiagnosticContext;
        });
        app.UseMiddleware<GlobalExceptionMiddleware>();

        if (IsSwaggerEnabled(app.Environment, app.Configuration))
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
    }

    private static bool IsSwaggerEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() || configuration.GetValue<bool>("EnableSwagger", false);

    private static LogEventLevel GetLogEventLevel(HttpContext ctx, Exception? ex)
    {
        if (ex is not null || ctx.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        return ctx.Response.StatusCode >= 400 ? LogEventLevel.Warning : LogEventLevel.Information;
    }

    private static void EnrichDiagnosticContext(IDiagnosticContext diagnosticContext, HttpContext ctx)
    {
        if (ctx.Items.TryGetValue("X-Correlation-Id", out var id))
        {
            diagnosticContext.Set("CorrelationId", id?.ToString());
        }
    }
}
