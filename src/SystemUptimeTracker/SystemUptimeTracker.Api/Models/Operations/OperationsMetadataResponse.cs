namespace SystemUptimeTracker.Api.Models.Operations;

public sealed class OperationsMetadataResponse
{
    public string ApplicationName { get; init; } = string.Empty;

    public string ApplicationVersion { get; init; } = string.Empty;

    public string BuildVersion { get; init; } = string.Empty;

    public string Environment { get; init; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; init; }
}