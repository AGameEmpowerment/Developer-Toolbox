namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

public class ForwardedHeaders
{
    public string[] KnownIpNetworks { get; set; } = [];
    public string[] KnownProxies { get; set; } = [];
}