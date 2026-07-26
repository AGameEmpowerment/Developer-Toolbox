using Newtonsoft.Json;
using SystemUptimeTracker.Common.Helpers.Data;
using System.Xml.Serialization;

namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

/*
 * To use the Visual Studio build profiles, install the Visual Studio Extension "SlowCheetah"
 * Additionally to change build environment update to use the "CentralApi.Web (Build Config)" from drop down.
 *
 * Keyvault variable name is separated with "--". Example: ConnectionStrings--DefaultConnection
 *
 * When mapping Environment variables to appsettings.json, use the following format: ConnectionStrings__DefaultConnection where the "__" is treated similarly to the "--" in the keyvault.
 * This will map the environment variable ConnectionStrings__DefaultConnection to the DefaultConnection key in appsettings.json
 */

public class AppSettings
{
    [JsonIgnore]
    [XmlIgnore]
    public IConfiguration ConfigurationBase { get; set; } = null!;

    public Logging Logging { get; set; } = new();
    public string AllowedHosts { get; set; } = string.Empty;
    public string ImpersonatingCookie { get; set; } = string.Empty;

    [SensitiveData]
    public string RedactionKey { get; set; } = string.Empty;

    public string KeyVaultUri { get; set; } = string.Empty;
    public FeatureManagement FeatureManagement { get; set; } = new();
    public ForwardedHeaders ForwardedHeaders { get; set; } = new();
    public Cors Cors { get; set; } = new();
    public Opentelemetry OpenTelemetry { get; set; } = new();
    public ConnectionStrings ConnectionStrings { get; set; } = new();
    public DataProtectionSettings DataProtection { get; set; } = new();
    public Auth Auth { get; set; } = new();
    public HttpClients HttpClients { get; set; } = new();
    public CacheSettings Cache { get; set; } = new();
    public Redis Redis { get; set; } = new();
}
