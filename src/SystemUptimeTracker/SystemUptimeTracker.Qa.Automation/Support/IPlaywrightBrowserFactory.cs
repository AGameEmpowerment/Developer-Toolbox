using Microsoft.Playwright;

namespace SystemUptimeTracker.Qa.Automation.Support
{
    public interface IPlaywrightBrowserFactory : IAsyncDisposable
    {
        Task<IBrowser> GetBrowserAsync();
    }
}