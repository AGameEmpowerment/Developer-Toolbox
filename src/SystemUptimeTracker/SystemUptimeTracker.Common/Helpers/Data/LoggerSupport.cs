using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using System.Reflection;

namespace SystemUptimeTracker.Common.Helpers.Data;

public static class LoggerSupport
{
    private const string SEQ_API_KEY_HEADER_NAME = "X-Seq-ApiKey";
    private static readonly string[] _otlpSignalPaths =
    [
        "/v1/logs",
        "/v1/traces",
        "/v1/metrics"
    ];

    /// <summary>
    /// Builds the optional Seq OTLP API-key header when a real key has been configured.
    /// </summary>
    /// <param name="apiKey">The configured Seq API key.</param>
    /// <returns>The OTLP exporter header string in <c>X-Seq-ApiKey=value</c> form, or an empty string when no usable key is configured.</returns>
    public static string BuildSeqApiKeyHeader(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) ||
            apiKey.Contains("Replace-Key", StringComparison.OrdinalIgnoreCase) ||
            apiKey.Contains("replace_with", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return $"{SEQ_API_KEY_HEADER_NAME}={apiKey.Trim()}";
    }

    public static Uri? BuildOtlpSignalEndpoint(string? endpoint, string? signalName)
    {
        string normalizedEndpoint = endpoint?.Trim() ?? string.Empty;
        string normalizedSignalName = signalName?.Trim().Trim('/') ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedEndpoint) ||
            string.IsNullOrWhiteSpace(normalizedSignalName) ||
            !Uri.TryCreate(normalizedEndpoint, UriKind.Absolute, out Uri? parsedEndpoint) ||
            (parsedEndpoint.Scheme != Uri.UriSchemeHttp && parsedEndpoint.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        string signalPath = $"/v1/{normalizedSignalName}";
        UriBuilder builder = new(parsedEndpoint);
        string normalizedPath = parsedEndpoint.AbsolutePath.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            builder.Path = signalPath;
            return builder.Uri;
        }

        foreach (string existingSignalPath in _otlpSignalPaths)
        {
            if (!normalizedPath.EndsWith(existingSignalPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Path = normalizedPath[..^existingSignalPath.Length] + signalPath;
            return builder.Uri;
        }

        builder.Path = normalizedPath + signalPath;
        return builder.Uri;
    }

    /// <summary>
    /// Override the default logging factory to enable logging of SQL queries.
    /// </summary>
    /// <returns></returns>
    public static ILoggerFactory GetLoggerFactory(IServiceCollection? roServiceCollection)
    {
        IServiceCollection serviceCollection = new ServiceCollection();

        ServiceProvider? serviceProvider = roServiceCollection?.BuildServiceProvider();
        IConfiguration? configuration = serviceProvider?.GetService<IConfiguration>();
        bool openTelemetryEnabled = configuration != null &&
                                    configuration.GetValue<bool>($"FeatureManagement:{Constants.FeatureFlags.OPEN_TELEMETRY_ENABLED}");
        bool openTelemetrySeqEnabled = configuration != null &&
                                       configuration.GetValue<bool>($"FeatureManagement:{Constants.FeatureFlags.OPEN_TELEMETRY_SEQ_ENABLED}");

        if (openTelemetryEnabled && openTelemetrySeqEnabled)
        {
            string endpoint = configuration?.GetValue<string>("OpenTelemetry:Endpoint") ?? string.Empty;
            string apiKey = configuration?.GetValue<string>("OpenTelemetry:ApiKey") ?? string.Empty;
            bool includeScopes = configuration?.GetValue("OpenTelemetry:IncludeScopes", true) ?? true;
            bool includeFormattedMessage = configuration?.GetValue("OpenTelemetry:IncludeFormattedMessage", true) ?? true;
            bool parseStateValues = configuration?.GetValue("OpenTelemetry:ParseStateValues", true) ?? true;

            serviceCollection.AddLogging(builder =>
            {
                builder.AddOpenTelemetry(x =>
                    {
                        x.SetResourceBuilder(ResourceBuilder.CreateEmpty()
                            .AddService(
                                Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown",
                                autoGenerateServiceInstanceId: false)
                            .AddAttributes(new Dictionary<string, object>()
                            {
                                ["service.execution"] = "sql",
                                ["deployment.environment"] = configuration?.GetValue<string>("Environment") ?? "Unknown",
                                ["deployment.version"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0.0"
                            }));

                        x.IncludeScopes = includeScopes;
                        x.IncludeFormattedMessage = includeFormattedMessage;
                        x.ParseStateValues = parseStateValues;

                        x.AddConsoleExporter();
                        if (string.IsNullOrWhiteSpace(endpoint))
                        {
                            return;
                        }

                        x.AddOtlpExporter(a =>
                        {
                            a.Endpoint = new Uri(endpoint);
                            a.Protocol = OtlpExportProtocol.HttpProtobuf;
                            string headers = BuildSeqApiKeyHeader(apiKey);
                            if (!string.IsNullOrWhiteSpace(headers))
                            {
                                a.Headers = headers;
                            }
                        });
                    })
                    .AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Debug)
                    .AddFilter(DbLoggerCategory.Query.Name, LogLevel.Debug)
                    .AddFilter(DbLoggerCategory.Update.Name, LogLevel.Debug);
            });
        }
        else
        {
            // Fallback to default logging if no ServiceCollection is provided.
            if (roServiceCollection == null)
            {
                serviceCollection.AddLogging(builder =>
                {
                    builder
                        .AddDebug()
                        .AddConsole()
                        .AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Debug)
                        .AddFilter(DbLoggerCategory.Query.Name, LogLevel.Debug)
                        .AddFilter(DbLoggerCategory.Update.Name, LogLevel.Debug);
                });
            }
        }

        return serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>()!;
    }

    /// <summary>
    /// Override the default logging factory to enable logging of SQL queries.
    /// </summary>
    /// <returns></returns>
    public static ILoggerFactory GetLoggerFactory()
    {
        IServiceCollection serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder =>
        {
            builder
                .AddDebug()
                .AddConsole()
                .AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Debug)
                .AddFilter(DbLoggerCategory.Query.Name, LogLevel.Debug)
                .AddFilter(DbLoggerCategory.Update.Name, LogLevel.Debug);
        });

        return serviceCollection
            .BuildServiceProvider()
            .GetService<ILoggerFactory>()!;
    }
}
