using SystemUptimeTracker.Common.Helpers.Data;

namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

public class Opentelemetry
{
    public string Endpoint { get; set; } = string.Empty;

    [SensitiveData]
    public string ApiKey { get; set; } = string.Empty;

    public bool ExportDebugLogs { get; set; } = true;

    public bool IncludeScopes { get; set; } = true;

    public bool IncludeFormattedMessage { get; set; } = true;

    public bool ParseStateValues { get; set; } = true;
}
