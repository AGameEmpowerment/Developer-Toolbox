namespace SystemUptimeTracker.Qa.Automation.Support
{
    public sealed class AutomationAppSettings
    {
        public string BaseUrl { get; init; } = "https://localhost:7060/";

        public LoginCredentials Credentials { get; init; } = new();

        public LocalIdentityTestUserConfiguration LocalIdentityTestUser { get; init; } = new();

        public WebBrowserConfiguration WebBrowserConfiguration { get; init; } = new();

        public ApiClientConfiguration ApiClientConfiguration { get; init; } = new();
    }
}