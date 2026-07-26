using Microsoft.Playwright;

namespace SystemUptimeTracker.Qa.Automation.Support
{
    public interface IPlaywrightPageSession : IAsyncDisposable
    {
        IPage Page { get; }

        TPage CreatePage<TPage>()
            where TPage : BasePage;
    }
}