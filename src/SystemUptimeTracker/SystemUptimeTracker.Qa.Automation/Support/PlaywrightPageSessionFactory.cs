using Microsoft.Playwright;

namespace SystemUptimeTracker.Qa.Automation.Support
{
    internal sealed class PlaywrightPageSessionFactory : IPlaywrightPageSessionFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IPlaywrightBrowserFactory _browserFactory;
        private readonly IPlaywrightBrowserEnvironment _environment;

        public PlaywrightPageSessionFactory(
            IServiceProvider serviceProvider,
            IPlaywrightBrowserFactory browserFactory,
            IPlaywrightBrowserEnvironment environment)
        {
            _serviceProvider = serviceProvider;
            _browserFactory = browserFactory;
            _environment = environment;
        }

        public async Task<IPlaywrightPageSession> CreateAsync()
        {
            IBrowser browser = await _browserFactory.GetBrowserAsync();
            WebBrowserConfiguration configuration = _environment.BrowserConfiguration;
            IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true
            });

            IPage page = await context.NewPageAsync();
            page.SetDefaultNavigationTimeout(configuration.PageLoadTimeoutInSeconds * 1000);
            page.SetDefaultTimeout(configuration.ImplicitWaitInSeconds * 1000);

            return new PlaywrightPageSession(_serviceProvider, context, page);
        }
    }
}