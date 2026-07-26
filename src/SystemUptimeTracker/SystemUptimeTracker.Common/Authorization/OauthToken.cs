using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;

namespace SystemUptimeTracker.Common.Authorization;

[ExcludeFromCodeCoverage]
public class OauthToken
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonProperty("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }

    public string Scope { get; set; } = string.Empty;
}
