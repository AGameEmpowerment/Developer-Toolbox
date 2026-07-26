namespace SystemUptimeTracker.Qa.Automation.Support
{
    public interface IPlaywrightBrowserEnvironment
    {
        WebBrowserConfiguration BrowserConfiguration { get; }
    }
}