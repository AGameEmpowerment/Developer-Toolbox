using System.Text.Json.Serialization;

namespace SystemUptimeTracker.Api.Models.Ui.InfoPage;

public class Service
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("encoded-name")]
    public string? EncodedName { get; set; }

    [JsonPropertyName("status")]
    public Status Status { get; set; }

    [JsonPropertyName("response-time")]
    public string? ResponseTime { get; set; }

    [JsonPropertyName("messages")]
    public Messages? Messages { get; set; }
}