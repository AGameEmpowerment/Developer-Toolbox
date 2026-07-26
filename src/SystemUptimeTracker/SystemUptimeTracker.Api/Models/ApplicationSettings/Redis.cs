namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

public class Redis
{
    public bool LocalOverride { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string DefaultCacheDurationMinutes { get; set; } = string.Empty;
}
