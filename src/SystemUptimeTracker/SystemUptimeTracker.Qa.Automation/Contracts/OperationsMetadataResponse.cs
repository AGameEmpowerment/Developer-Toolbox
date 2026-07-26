namespace SystemUptimeTracker.Qa.Automation.Contracts;

public sealed class OperationsMetadataResponse
{
    public string ApplicationName { get; set; } = string.Empty;

    public string ApplicationVersion { get; set; } = string.Empty;

    public string BuildVersion { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }
}