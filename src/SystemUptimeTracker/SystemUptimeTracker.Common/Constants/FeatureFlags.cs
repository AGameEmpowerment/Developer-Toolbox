using System.Diagnostics.CodeAnalysis;

namespace SystemUptimeTracker.Common.Constants;

[ExcludeFromCodeCoverage]
public class FeatureFlags
{
    public const string ASPIRE_ENABLED = "AspireEnabled";
    public const string CONFIGURATION_INFO_ENABLED = "ConfigurationInfoEnabled";
    public const string INFO_ENDPOINT_ENABLED = "InfoEndpointEnabled";
    public const string OPEN_API_ENABLED = "OpenApiEnabled";
    public const string OPEN_TELEMETRY_ENABLED = "OpenTelemetryEnabled";
    public const string OPEN_TELEMETRY_SEQ_ENABLED = "OpenTelemetrySeqEnabled";
    public const string SQL_DEBUGGER = "SqlDebugger";
}
