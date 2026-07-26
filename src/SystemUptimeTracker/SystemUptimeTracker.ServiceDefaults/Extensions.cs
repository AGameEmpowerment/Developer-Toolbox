using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SystemUptimeTracker.ServiceDefaults;

/// <summary>
/// Provides shared defaults that are only enabled when SystemUptimeTracker runs under Aspire orchestration.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Adds shared Aspire defaults such as OTLP telemetry export, service discovery, and a basic liveness check.
    /// </summary>
    /// <typeparam name="TBuilder">The host builder type.</typeparam>
    /// <param name="builder">The host builder instance.</param>
    /// <param name="enableOpenTelemetry">Determines whether OpenTelemetry should be configured for this host.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public static TBuilder AddSystemUptimeTrackerServiceDefaults<TBuilder>(this TBuilder builder, bool enableOpenTelemetry = true)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (enableOpenTelemetry)
        {
            builder.ConfigureSystemUptimeTrackerOpenTelemetry();
        }

        builder.Services.AddServiceDiscovery();

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    private static TBuilder ConfigureSystemUptimeTrackerOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        bool useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        string serviceName = ResolveServiceName(builder);

        builder.Logging.AddOpenTelemetry();

        builder.Services.Configure<OpenTelemetryLoggerOptions>(options =>
        {
            options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(
                serviceName,
                autoGenerateServiceInstanceId: false));
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;

            if (useOtlpExporter)
            {
                options.AddOtlpExporter();
            }
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName,
                autoGenerateServiceInstanceId: false))
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (useOtlpExporter)
                {
                    metrics.AddOtlpExporter();
                }
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (useOtlpExporter)
                {
                    tracing.AddOtlpExporter();
                }
            });

        return builder;
    }

    private static string ResolveServiceName<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        string? configuredServiceName = builder.Configuration["OTEL_SERVICE_NAME"]
            ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");

        if (!string.IsNullOrWhiteSpace(configuredServiceName))
        {
            return configuredServiceName;
        }

        string? resourceAttributes = builder.Configuration["OTEL_RESOURCE_ATTRIBUTES"]
            ?? Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES");

        string? serviceNameAttribute = TryGetResourceAttributeValue(resourceAttributes, "service.name");

        return !string.IsNullOrWhiteSpace(serviceNameAttribute)
            ? serviceNameAttribute
            : builder.Environment.ApplicationName;
    }

    private static string? TryGetResourceAttributeValue(string? resourceAttributes, string key)
    {
        if (string.IsNullOrWhiteSpace(resourceAttributes))
        {
            return null;
        }

        foreach (string attribute in resourceAttributes.Split(','))
        {
            int separatorIndex = attribute.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string attributeKey = attribute[..separatorIndex].Trim();
            if (!string.Equals(attributeKey, key, StringComparison.Ordinal))
            {
                continue;
            }

            string value = attribute[(separatorIndex + 1)..].Trim();
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }

        return null;
    }
}
