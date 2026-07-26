using System.Text.Json.Serialization;

namespace SystemUptimeTracker.Api.Models.Ui.InfoPage;

/// <summary>
/// 
/// </summary>
public class Messages
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}