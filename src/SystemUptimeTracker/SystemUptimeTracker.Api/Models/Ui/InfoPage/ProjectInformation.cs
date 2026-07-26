using System.Text.Json.Serialization;

namespace SystemUptimeTracker.Api.Models.Ui.InfoPage;

public class ProjectInformation
{
    [JsonPropertyName("info")]
    public List<Info>? Info { get; set; }
}