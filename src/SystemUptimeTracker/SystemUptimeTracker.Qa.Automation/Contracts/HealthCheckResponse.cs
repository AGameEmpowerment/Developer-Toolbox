using System.Collections.Generic;
using System.Text.Json;

namespace SystemUptimeTracker.Qa.Automation.Contracts;

public sealed class HealthCheckResponse
{
    public string Status { get; set; } = string.Empty;

    public string TotalDuration { get; set; } = string.Empty;

    public Dictionary<string, JsonElement> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
