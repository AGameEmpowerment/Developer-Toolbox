using Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Reflection;

const string OTLP_SERVICE_NAME_ENVIRONMENT_VARIABLE = "OTEL_SERVICE_NAME";
const string OTLP_RESOURCE_ATTRIBUTES_ENVIRONMENT_VARIABLE = "OTEL_RESOURCE_ATTRIBUTES";
const string SHARED_CONNECTION_STRING_PLACEHOLDER = "__SET_IN_USER_SECRETS_OR_ENV__";
const string DEFAULT_CONNECTION_ENVIRONMENT_VARIABLE = "ConnectionStrings__DefaultConnection";

var builder = DistributedApplication.CreateBuilder(args);

if (!builder.Environment.IsProduction())
{
    builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true, reloadOnChange: true);
}

// Allow QA automation to override AppHost resource settings via process-level
// environment variables after local secrets have been loaded.
builder.Configuration.AddEnvironmentVariables();

IConfigurationSection appHostSettings = builder.Configuration.GetRequiredSection("AppHost");
IConfigurationSection serverSettings = appHostSettings.GetRequiredSection("Server");
IConfigurationSection clientSettings = appHostSettings.GetRequiredSection("Client");
IConfigurationSection serverEnvironmentVariables = serverSettings.GetRequiredSection("EnvironmentVariables");
IConfigurationSection clientEnvironmentVariables = clientSettings.GetRequiredSection("EnvironmentVariables");
string serverName = GetRequiredString(serverSettings, "Name");
int serverPort = GetRequiredInt(serverSettings, "Port");
string serverUrl = string.Create(CultureInfo.InvariantCulture, $"https://localhost:{serverPort}");
string serverDefaultConnectionString = ResolveEnvironmentVariableValue(
    builder.Configuration,
    serverEnvironmentVariables.GetRequiredSection(DEFAULT_CONNECTION_ENVIRONMENT_VARIABLE));

var server = builder.AddProject<Projects.SystemUptimeTracker_Api>(serverName)
    .WithHttpsEndpoint(
        targetPort: serverPort,
        port: serverPort,
        name: "https",
        env: "ASPNETCORE_HTTPS_PORT",
        isProxied: false);

string serverUrls = serverUrl;
if (builder.Environment.IsDevelopment())
{
    int serverHttpPort = GetRequiredInt(serverSettings, "HttpPort");
    string serverHttpUrl = string.Create(CultureInfo.InvariantCulture, $"http://localhost:{serverHttpPort}");
    server = server.WithHttpEndpoint(
        targetPort: serverHttpPort,
        port: serverHttpPort,
        name: "http",
        env: "ASPNETCORE_HTTP_PORT",
        isProxied: false);
    serverUrls = $"{serverUrl};{serverHttpUrl}";
}

server = server
    .WithArgs($"--ConnectionStrings:DefaultConnection={serverDefaultConnectionString}")
    .WithEnvironment("ASPNETCORE_URLS", serverUrls)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithExternalHttpEndpoints();

server = server.WithHttpHealthCheck(
    () => server.GetEndpoint("https"),
    GetRequiredString(serverSettings, "HealthCheckPath"));

foreach (IConfigurationSection environmentVariable in serverEnvironmentVariables.GetChildren())
{
    string resolvedValue = ResolveEnvironmentVariableValue(builder.Configuration, environmentVariable);
    server = server.WithEnvironment(environmentVariable.Key, resolvedValue);
}

string? configuredServerOtelServiceName = serverEnvironmentVariables[OTLP_SERVICE_NAME_ENVIRONMENT_VARIABLE]
    ?? Environment.GetEnvironmentVariable(OTLP_SERVICE_NAME_ENVIRONMENT_VARIABLE);
string? configuredServerOtelResourceAttributes = serverEnvironmentVariables[OTLP_RESOURCE_ATTRIBUTES_ENVIRONMENT_VARIABLE]
    ?? Environment.GetEnvironmentVariable(OTLP_RESOURCE_ATTRIBUTES_ENVIRONMENT_VARIABLE);
string? configuredServerOtlpEndpoint = serverEnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"]
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
string? aspireDashboardOtlpEndpoint = Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL");

if (string.IsNullOrWhiteSpace(configuredServerOtlpEndpoint)
    && !string.IsNullOrWhiteSpace(aspireDashboardOtlpEndpoint))
{
    server = server.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", aspireDashboardOtlpEndpoint);
    server = server.WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");
}

if (string.IsNullOrWhiteSpace(configuredServerOtelServiceName)
    && !ContainsServiceNameAttribute(configuredServerOtelResourceAttributes))
{
    server = server.WithEnvironment(OTLP_SERVICE_NAME_ENVIRONMENT_VARIABLE, serverName);
}

int clientPort = GetRequiredInt(clientSettings, "Port");
string clientName = GetRequiredString(clientSettings, "Name");
string clientAppDirectory = ResolvePathRelativeToAppHost(builder, GetRequiredString(clientSettings, "AppDirectory"));

var client = builder.AddJavaScriptApp(
        name: clientName,
        appDirectory: clientAppDirectory,
        runScriptName: GetRequiredString(clientSettings, "RunScriptName"))
    .WithHttpsEndpoint(
        port: clientPort,
        targetPort: clientPort,
        env: GetRequiredString(clientSettings, "PortEnvironmentVariable"),
        isProxied: clientSettings.GetValue("IsProxied", true))
    .WithExternalHttpEndpoints();

foreach (IConfigurationSection environmentVariable in clientEnvironmentVariables.GetChildren())
{
    client = client.WithEnvironment(environmentVariable.Key, environmentVariable.Value ?? string.Empty);
}

string? configuredClientAspireEndpoint = clientEnvironmentVariables["APP_OPEN_TELEMETRY_ASPIRE_ENDPOINT"]
    ?? Environment.GetEnvironmentVariable("APP_OPEN_TELEMETRY_ASPIRE_ENDPOINT");
string? configuredClientOtlpEndpoint = clientEnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"]
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
string? configuredClientOtelServiceName = clientEnvironmentVariables[OTLP_SERVICE_NAME_ENVIRONMENT_VARIABLE]
    ?? Environment.GetEnvironmentVariable(OTLP_SERVICE_NAME_ENVIRONMENT_VARIABLE);
string? configuredClientOtelResourceAttributes = clientEnvironmentVariables[OTLP_RESOURCE_ATTRIBUTES_ENVIRONMENT_VARIABLE]
    ?? Environment.GetEnvironmentVariable(OTLP_RESOURCE_ATTRIBUTES_ENVIRONMENT_VARIABLE);
string? aspireDashboardOtlpHttpEndpoint = Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL");

if (string.IsNullOrWhiteSpace(configuredClientAspireEndpoint)
    && string.IsNullOrWhiteSpace(configuredClientOtlpEndpoint)
    && !string.IsNullOrWhiteSpace(aspireDashboardOtlpHttpEndpoint))
{
    client = client.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", aspireDashboardOtlpHttpEndpoint);
}

if (string.IsNullOrWhiteSpace(configuredClientOtelServiceName)
    && !ContainsServiceNameAttribute(configuredClientOtelResourceAttributes))
{
    client = client.WithEnvironment(OTLP_SERVICE_NAME_ENVIRONMENT_VARIABLE, clientName);
}

client.WaitFor(server);

await builder.Build().RunAsync();

static string GetRequiredString(IConfigurationSection configuration, string key)
{
    string? value = configuration[key];
    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"Missing required AppHost configuration value '{configuration.Path}:{key}'.");
}

static int GetRequiredInt(IConfigurationSection configuration, string key)
{
    int? value = configuration.GetValue<int?>(key);
    return value ?? throw new InvalidOperationException($"Missing required AppHost configuration value '{configuration.Path}:{key}'.");
}

static string ResolvePathRelativeToAppHost(IDistributedApplicationBuilder builder, string path)
{
    return Path.GetFullPath(Path.IsPathRooted(path)
        ? path
        : Path.Combine(builder.AppHostDirectory, path));
}

static bool ContainsServiceNameAttribute(string? resourceAttributes)
{
    if (string.IsNullOrWhiteSpace(resourceAttributes))
    {
        return false;
    }

    foreach (string attribute in resourceAttributes.Split(','))
    {
        int separatorIndex = attribute.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        string key = attribute[..separatorIndex].Trim();
        if (string.Equals(key, "service.name", StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}

static string ResolveEnvironmentVariableValue(IConfiguration configurationRoot, IConfigurationSection configuration)
{
    string key = configuration.Key;
    string? configuredValue = configuration.Value;

    if (!string.Equals(key, DEFAULT_CONNECTION_ENVIRONMENT_VARIABLE, StringComparison.Ordinal))
    {
        return configuredValue ?? string.Empty;
    }

    if (!string.IsNullOrWhiteSpace(configuredValue)
        && !string.Equals(configuredValue, SHARED_CONNECTION_STRING_PLACEHOLDER, StringComparison.OrdinalIgnoreCase))
    {
        return configuredValue;
    }

    string? configuredSecretValue = Environment.GetEnvironmentVariable(DEFAULT_CONNECTION_ENVIRONMENT_VARIABLE)
        ?? configurationRoot.GetConnectionString("DefaultConnection")
        ?? configurationRoot["ConnectionStrings:DefaultConnection"]
        ?? configurationRoot[DEFAULT_CONNECTION_ENVIRONMENT_VARIABLE];
    return !string.IsNullOrWhiteSpace(configuredSecretValue)
        ? configuredSecretValue
        : throw new InvalidOperationException(
            $"Missing required AppHost connection string. Set '{DEFAULT_CONNECTION_ENVIRONMENT_VARIABLE}' in user secrets or the environment before launching AppHost.");
}
