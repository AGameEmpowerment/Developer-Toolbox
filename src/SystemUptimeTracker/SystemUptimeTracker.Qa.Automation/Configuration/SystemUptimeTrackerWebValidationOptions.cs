using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SystemUptimeTracker.Qa.Automation.Configuration;

public sealed class SystemUptimeTrackerWebValidationOptions
{
    public const string SECTION_NAME = "TestConfiguration:WebValidation";

    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    public bool UseInternalPages { get; set; }

    public Dictionary<string, string> Pages { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Titles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
