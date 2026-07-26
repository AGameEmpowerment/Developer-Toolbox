using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace SystemUptimeTracker.Qa.Automation.Support
{
    internal sealed class PageObjectFactory : IPageObjectFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public PageObjectFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public TPage CreatePage<TPage>(IPage page)
            where TPage : BasePage
        {
            return ActivatorUtilities.CreateInstance<TPage>(_serviceProvider, page);
        }
    }
}