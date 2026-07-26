namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

public class Auth
{
    public Jwt Jwt { get; set; } = new();
    public int LoginTimeInMinutes { get; set; }
}
