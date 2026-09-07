namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

public class HttpClients
{
    public Resilience Resilience { get; set; } = new();
}
