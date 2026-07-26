using Microsoft.Extensions.DependencyInjection;

namespace SystemUptimeTracker.Qa.Automation.Support
{
    public abstract class QaPlaywrightTestBase : QaApiTestBase
    {
        private IPlaywrightPageSession? _playwrightSession;

        protected Microsoft.Playwright.IPage Page => _playwrightSession?.Page ??
                                                     throw new InvalidOperationException("Playwright page session has not been created.");

        protected async Task CreatePlaywrightSessionAsync()
        {
            _playwrightSession = await Services.GetRequiredService<IPlaywrightPageSessionFactory>().CreateAsync();
        }

        protected async Task DisposePlaywrightSessionAsync()
        {
            if (_playwrightSession is not null)
            {
                await _playwrightSession.DisposeAsync();
                _playwrightSession = null;
            }
        }

        protected TPage CreatePage<TPage>()
            where TPage : BasePage
        {
            if (_playwrightSession is null)
            {
                throw new InvalidOperationException("Playwright page session has not been created.");
            }

            return _playwrightSession.CreatePage<TPage>();
        }
    }
}