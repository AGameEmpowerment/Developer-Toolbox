using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace SystemUptimeTracker.Qa.Automation.Support
{
    public abstract class BasePage
    {
        protected BasePage(
            IPage page,
            IPageObjectFactory pageObjectFactory)
        {
            Page = page ?? throw new ArgumentNullException(nameof(page));
            _pageObjectFactory = pageObjectFactory ?? throw new ArgumentNullException(nameof(pageObjectFactory));
        }

        private readonly IPageObjectFactory _pageObjectFactory;

        protected IPage Page { get; }

        public abstract string PageTitle { get; protected set; }

        public abstract string PageUrl { get; protected set; }

        public async Task NavigateToAsync()
        {
            await Page.GotoAsync(PageUrl);
            await Assertions.Expect(Page).ToHaveTitleAsync(new Regex(Regex.Escape(PageTitle), RegexOptions.IgnoreCase));
        }

        protected TPage CreatePage<TPage>()
            where TPage : BasePage
        {
            return _pageObjectFactory.CreatePage<TPage>(Page);
        }
    }
}