using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SystemUptimeTracker.Api;
using SystemUptimeTracker.Api.Extensions;
using SystemUptimeTracker.Api.Models.ApplicationSettings;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

WebApplication app = builder
    .RegisterServices(args, out AppSettings appSettings)
    .Build();

ILogger startupLogger = app.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("Startup");

string applicationInsightsConnectionString = appSettings.ConnectionStrings.ApplicationInsights;
bool hasApplicationInsights = RegisterDependentServices.HasConfiguredApplicationInsights(applicationInsightsConnectionString);

startupLogger.LogDebug(
    "Backend application startup completed. Environment: {EnvironmentName}; ApplicationInsightsConfigured: {ApplicationInsightsConfigured}",
    app.Environment.EnvironmentName,
    hasApplicationInsights);

if (appSettings.FeatureManagement.OpenTelemetryEnabled)
{
    startupLogger.LogInformation(
        "OpenTelemetry logging is enabled. SeqEnabled={SeqEnabled}; EndpointConfigured={EndpointConfigured}; ExportDebugLogs={ExportDebugLogs}; IncludeScopes={IncludeScopes}; IncludeFormattedMessage={IncludeFormattedMessage}; ParseStateValues={ParseStateValues}.",
        appSettings.FeatureManagement.OpenTelemetrySeqEnabled,
        !string.IsNullOrWhiteSpace(appSettings.OpenTelemetry.Endpoint),
        appSettings.OpenTelemetry.ExportDebugLogs,
        appSettings.OpenTelemetry.IncludeScopes,
        appSettings.OpenTelemetry.IncludeFormattedMessage,
        appSettings.OpenTelemetry.ParseStateValues);
}

await app.InitializeIdentityAsync();

await app
    .SetupMiddleware(appSettings)
    .RunAsync();
