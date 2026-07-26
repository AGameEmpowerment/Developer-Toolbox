using Microsoft.Playwright;

namespace SystemUptimeTracker.Qa.Automation.Support
{
    public interface IPageObjectFactory
    {
        TPage CreatePage<TPage>(IPage page)
            where TPage : BasePage;
    }
}