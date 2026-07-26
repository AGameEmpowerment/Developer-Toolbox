namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

using SystemUptimeTracker.Common.Helpers.Data;

public sealed class Jwt
{
    public bool Enabled { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    [SensitiveData]
    public string SigningKey { get; set; } = string.Empty;
    public int ClockSkewSeconds { get; set; } = 60;
}
