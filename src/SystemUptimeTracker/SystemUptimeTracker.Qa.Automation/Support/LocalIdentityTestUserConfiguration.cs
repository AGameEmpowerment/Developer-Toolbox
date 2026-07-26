namespace SystemUptimeTracker.Qa.Automation.Support
{
    public sealed class LocalIdentityTestUserConfiguration
    {
        public string EmailLocalPartPrefix { get; init; } = "systemuptimetracker-qa";

        public string EmailDomain { get; init; } = "example.invalid";

        public string[] RequiredRoles { get; init; } = ["Admin"];
    }
}