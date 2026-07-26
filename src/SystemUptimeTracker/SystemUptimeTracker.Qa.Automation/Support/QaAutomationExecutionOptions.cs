namespace SystemUptimeTracker.Qa.Automation.Support;

public sealed class QaAutomationExecutionOptions
{
    public const string SECTION_NAME = "QaAutomation";

    public bool UseExternalHost { get; set; }

    public bool SkipDatabaseCleanup { get; set; }

    public bool SkipIdentityCleanup { get; set; }

    public bool AllowMainDatabase { get; set; }

    public string WebBaseUrl { get; set; } = string.Empty;
}
