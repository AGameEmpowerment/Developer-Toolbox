namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

public class Logging
{
    public LoggingLevels LogLevel { get; set; } = new();
}
