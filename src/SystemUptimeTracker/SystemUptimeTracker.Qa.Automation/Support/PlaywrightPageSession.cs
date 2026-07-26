using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace SystemUptimeTracker.Qa.Automation.Support
{
    internal sealed class PlaywrightPageSession : IPlaywrightPageSession
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IBrowserContext _context;

        public PlaywrightPageSession(IServiceProvider serviceProvider, IBrowserContext context, IPage page)
        {
            _serviceProvider = serviceProvider;
            _context = context;
            Page = page;
        }

        public IPage Page { get; }

        public TPage CreatePage<TPage>()
            where TPage : BasePage
        {
            return _serviceProvider.GetRequiredService<IPageObjectFactory>().CreatePage<TPage>(Page);
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
        }
    }
}