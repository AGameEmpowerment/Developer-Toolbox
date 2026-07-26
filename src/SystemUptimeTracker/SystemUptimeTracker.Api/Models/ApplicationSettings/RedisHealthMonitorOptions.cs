namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

/// <summary>
/// Options for the Redis health-monitor background service.
/// </summary>
public class RedisHealthMonitorOptions
{
    /// <summary>
    /// Gets or sets the health-check interval, in seconds.
    /// </summary>
    public int IntervalSeconds { get; set; } = 5;
}
