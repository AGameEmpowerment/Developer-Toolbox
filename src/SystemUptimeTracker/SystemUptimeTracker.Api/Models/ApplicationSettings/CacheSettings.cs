namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

/// <summary>
/// Configuration settings for the application distributed-cache foundation.
/// </summary>
public class CacheSettings
{
    /// <summary>
    /// Gets or sets the Redis health-monitor configuration.
    /// </summary>
    public RedisHealthMonitorOptions HealthMonitor { get; set; } = new();
}
