namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

public class LoggingLevels
{
    public string Default { get; set; } = string.Empty;
    public string Microsoft { get; set; } = string.Empty;
    public string System { get; set; } = string.Empty;
    public string MicrosoftHostingLifetime { get; set; } = string.Empty;
}
