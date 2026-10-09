using Acme.Core.Platform;
using Acme.Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Acme.Core.Hosting;

/// <summary>
/// The Aspire service defaults, kept in Core so there is one shared setup: OpenTelemetry (traces, metrics, logs over OTLP),
/// structured JSON logs, health checks, HTTP resilience and service discovery.
/// </summary>
public static class ServiceDefaults
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder, string serviceName)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
        builder.Logging.Configure(options =>
            options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Health probes are noise; never record headers (they may carry cookies or tokens).
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal);
                })
                .AddHttpClientInstrumentation()
                .AddSource("Npgsql")
                .AddSource(PlatformTelemetry.ActivitySourceName));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        // Liveness has no checks (process only); readiness checks are tagged HealthTags.Ready.
        builder.Services.AddHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }
}

/// <summary>The platform's own telemetry source, for spans around messaging, jobs and tenant work.</summary>
public static class PlatformTelemetry
{
    public const string ActivitySourceName = PlatformInfo.Product + ".platform";

    /// <summary>The span attribute carrying the tenant. Never used as a metric dimension by default.</summary>
    public const string TenantTag = TelemetryTags.TenantId;
}
