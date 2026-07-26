namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

/// <summary>
/// Defines the feature flags available through Microsoft.FeatureManagement.
/// </summary>
public class FeatureManagement
{
    public bool AspireEnabled { get; set; }
    public bool ConfigurationInfoEnabled { get; set; }
    public bool InfoEndpointEnabled { get; set; }
    public bool OpenApiEnabled { get; set; }
    public bool OpenTelemetryEnabled { get; set; }
    // Local Development
    public bool OpenTelemetrySeqEnabled { get; set; }
    public bool SqlDebugger { get; set; }
}
