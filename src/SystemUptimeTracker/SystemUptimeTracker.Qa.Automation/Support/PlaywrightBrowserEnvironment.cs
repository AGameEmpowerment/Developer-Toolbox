using Microsoft.Extensions.Options;

namespace SystemUptimeTracker.Qa.Automation.Support
{
    internal sealed class PlaywrightBrowserEnvironment : IPlaywrightBrowserEnvironment
    {
        public PlaywrightBrowserEnvironment(IOptions<AutomationAppSettings> appSettings)
        {
            BrowserConfiguration = appSettings.Value.WebBrowserConfiguration;
        }

        public WebBrowserConfiguration BrowserConfiguration { get; }
    }
}