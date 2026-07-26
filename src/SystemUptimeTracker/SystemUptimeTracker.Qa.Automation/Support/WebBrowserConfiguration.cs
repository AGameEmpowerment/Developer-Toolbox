namespace SystemUptimeTracker.Qa.Automation.Support
{
    public sealed class WebBrowserConfiguration
    {
        public int PageLoadTimeoutInSeconds { get; init; } = 120;

        public int ImplicitWaitInSeconds { get; init; } = 10;

        public int JavascriptTimeoutInSeconds { get; init; } = 30;

        public int TimeoutCommandSecs { get; init; } = 60;

        public string BrowserType { get; init; } = "Chromium";

        public bool HeadlessBrowser { get; init; } = true;
    }
}