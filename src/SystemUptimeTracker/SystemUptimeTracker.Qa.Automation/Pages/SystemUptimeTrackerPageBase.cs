using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SystemUptimeTracker.Qa.Automation.Support;

namespace SystemUptimeTracker.Qa.Automation.Pages;

public abstract class SystemUptimeTrackerPageBase<TPage> : BasePage
{
    protected SystemUptimeTrackerPageBase(
        IPage page,
        IPageObjectFactory pageObjectFactory,
        ILogger<TPage> logger)
        : base(page, pageObjectFactory)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected ILogger<TPage> Logger { get; }

    public string CurrentUrl => Page.Url;

    protected void LogResolvedPageConfiguration()
    {
        Logger.LogDebug(
            "Configured page object {PageObject} with target URL {PageUrl} and title {PageTitle}.",
            GetType().Name,
            PageUrl,
            PageTitle);
    }

    protected void LogAction(string action, string target)
    {
        Logger.LogInformation(
            "{PageObject} performing action {Action} on {Target}.",
            GetType().Name,
            action,
            target);
    }

    protected void LogObservation(string messageTemplate, params object?[] args)
    {
        Logger.LogInformation(messageTemplate, args);
    }

    protected void LogPageLoaded(string detail)
    {
        Logger.LogInformation(
            "Loaded page object {PageObject} at {CurrentUrl}. {Detail}",
            GetType().Name,
            Page.Url,
            detail);
    }

    protected async Task ExpectVisibleAsync(ILocator locator)
    {
        await Assertions.Expect(locator).ToBeVisibleAsync();
    }

    protected async Task ExpectHiddenAsync(ILocator locator)
    {
        await Assertions.Expect(locator).ToBeHiddenAsync();
    }

    protected static async Task<bool> TryWaitForVisibleAsync(ILocator locator, float timeoutMilliseconds = 5000)
    {
        try
        {
            await locator.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = timeoutMilliseconds
            });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }
}
