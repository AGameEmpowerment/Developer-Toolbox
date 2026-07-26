using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SystemUptimeTracker.Qa.Automation.Configuration;
using SystemUptimeTracker.Qa.Automation.Support;
using System.Text.RegularExpressions;

namespace SystemUptimeTracker.Qa.Automation.Pages;

public sealed class SystemUptimeTrackerHomePage : SystemUptimeTrackerPageBase<SystemUptimeTrackerHomePage>
{
    public override string PageTitle { get; protected set; }
    public override string PageUrl { get; protected set; }

    public SystemUptimeTrackerHomePage(
        IPage page,
        IPageObjectFactory pageObjectFactory,
        ISystemUptimeTrackerPageCatalog pageCatalog,
        ILogger<SystemUptimeTrackerHomePage> logger)
        : base(page, pageObjectFactory, logger)
    {
        PageTitle = pageCatalog.GetPageTitle("Home", "System Uptime Tracker");
        PageUrl = pageCatalog.GetPageUrl("Home", "https://localhost:3001/");
        LogResolvedPageConfiguration();
    }

    private ILocator PageRoot => Page.Locator("#home-page");

    private ILocator SkipLink => Page.GetByRole(AriaRole.Link, new() { Name = "Skip to main content" });

    private ILocator PrimaryNavigation => Page.GetByRole(AriaRole.Navigation, new() { Name = "Primary navigation" });

    private ILocator NavigationToggle => PrimaryNavigation.GetByRole(AriaRole.Button, new() { Name = "Toggle navigation" });

    private ILocator NavigationAdminButton => PrimaryNavigation.GetByRole(AriaRole.Button, new() { Name = "Admin" });

    private ILocator NavigationAdminUsersLink => PrimaryNavigation.GetByRole(AriaRole.Link, new() { Name = "Users" });

    private ILocator NavigationSignInLink => PrimaryNavigation.GetByRole(AriaRole.Link, new() { Name = "Sign In" });

    private ILocator Heading => Page.Locator("#home-page-title");

    private ILocator OverviewCopy => Page.Locator("#home-page-overview-copy");

    private ILocator LoginLink => Page.Locator("#home-page-link-login");

    private ILocator AccountSection => Page.Locator("#home-page-account");

    private ILocator UserInfoSection => Page.Locator("#user-info");

    private ILocator SessionState => Page.Locator("#user-info-session-state");

    private ILocator SessionLink => Page.Locator("#user-info-session-link");

    private ILocator UserDisplayName => Page.Locator("#user-info-display-name");

    private ILocator RouteStubHeading => Page.GetByRole(AriaRole.Heading, new() { Level = 1 });

    private ILocator RouteStubStatus => Page.GetByRole(AriaRole.Status);

    public async Task<string> GetHeadingTextAsync()
    {
        return (await Heading.InnerTextAsync()).Trim();
    }

    public async Task<string> GetOverviewCopyAsync()
    {
        return (await OverviewCopy.InnerTextAsync()).Trim();
    }

    public async Task<string> GetSessionStateTextAsync()
    {
        return (await SessionState.InnerTextAsync()).Trim();
    }

    public async Task<string?> GetSkipLinkTargetAsync()
    {
        return await SkipLink.GetAttributeAsync("href");
    }

    public async Task<string> GetUserDisplayNameAsync()
    {
        return (await UserDisplayName.InnerTextAsync()).Trim();
    }

    public async Task OpenSignInAsync()
    {
        LogAction("OpenPage", nameof(LoginPage));
        await LoginLink.ClickAsync();
    }

    public async Task SignOutAsync()
    {
        LogAction("OpenPage", "Logout");
        await SessionLink.ClickAsync();
    }

    public async Task<bool> HasSessionLinkAsync()
    {
        return await SessionLink.CountAsync() > 0;
    }

    public async Task<bool> HasAdminNavigationAsync()
    {
        return await NavigationAdminButton.CountAsync() > 0;
    }

    public async Task EnsurePrimaryNavigationExpandedAsync()
    {
        if (await TryWaitForVisibleAsync(NavigationAdminButton)
            || await TryWaitForVisibleAsync(NavigationSignInLink))
        {
            return;
        }

        if (!await TryWaitForVisibleAsync(NavigationToggle))
        {
            throw new InvalidOperationException("The primary navigation is collapsed, but the navigation toggle is not visible.");
        }

        string expanded = await NavigationToggle.GetAttributeAsync("aria-expanded") ?? "false";
        if (!string.Equals(expanded, "true", StringComparison.OrdinalIgnoreCase))
        {
            LogAction("Expand", "PrimaryNavigation");
            await NavigationToggle.ClickAsync();
        }
    }

    public async Task<bool> HasPrimaryNavigationSignInLinkAsync()
    {
        return await NavigationSignInLink.CountAsync() > 0;
    }

    public async Task OpenAdminUsersFromPrimaryNavigationAsync()
    {
        await EnsurePrimaryNavigationExpandedAsync();
        await Assertions.Expect(NavigationAdminButton).ToBeVisibleAsync();
        await ExpandAdminNavigationAsync();
        LogAction("OpenPage", "AdminUsersFromPrimaryNavigation");
        await NavigationAdminUsersLink.ClickAsync();
    }

    public async Task WaitForRouteStubAsync(string title, string route, string summarySnippet)
    {
        await Assertions.Expect(RouteStubHeading).ToHaveTextAsync(title);
        await Assertions.Expect(Page).ToHaveTitleAsync(new Regex($"^{Regex.Escape(title)}\\s*\\|\\s*System Uptime Tracker$", RegexOptions.IgnoreCase));
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"{Regex.Escape(route)}$", RegexOptions.IgnoreCase));
        await Assertions.Expect(RouteStubStatus).ToContainTextAsync(route);
        await Assertions.Expect(Page.GetByText(summarySnippet, new() { Exact = false })).ToBeVisibleAsync();
        LogObservation("Verified route stub {Title} at route {Route}.", title, route);
    }

    private async Task ExpandAdminNavigationAsync()
    {
        if (await TryWaitForVisibleAsync(NavigationAdminUsersLink))
        {
            return;
        }

        string expanded = await NavigationAdminButton.GetAttributeAsync("aria-expanded") ?? "false";
        if (!string.Equals(expanded, "true", StringComparison.OrdinalIgnoreCase))
        {
            LogAction("Expand", "AdminNavigation");
            await NavigationAdminButton.ClickAsync();
        }

        await Assertions.Expect(NavigationAdminUsersLink).ToBeVisibleAsync();
    }

    public async Task WaitForSignedInAsync()
    {
        await ExpectVisibleAsync(UserDisplayName);
        await Assertions.Expect(SessionLink).ToHaveTextAsync("Logout");
        LogPageLoaded("Verified the signed-in home page session state.");
    }

    public async Task WaitForSignedOutAsync()
    {
        await ExpectVisibleAsync(SessionState);
        await Assertions.Expect(SessionState).ToHaveTextAsync("No active session.");
        await Assertions.Expect(SessionLink).ToHaveTextAsync("Sign In");
        LogPageLoaded("Verified the signed-out home page session state.");
    }

    public async Task WaitForLoadedAsync()
    {
        await ExpectVisibleAsync(PageRoot);
        await ExpectVisibleAsync(SkipLink);
        await ExpectVisibleAsync(PrimaryNavigation);
        await ExpectVisibleAsync(Heading);
        await ExpectVisibleAsync(OverviewCopy);
        await ExpectVisibleAsync(AccountSection);
        await ExpectVisibleAsync(UserInfoSection);
        await ExpectVisibleAsync(SessionLink);
        LogPageLoaded("Verified the operational landing page and account access section.");
    }

    public async Task WaitForAdminNavigationHiddenAsync()
    {
        await Assertions.Expect(NavigationAdminButton).ToHaveCountAsync(0);
        LogObservation("Verified the admin navigation is hidden for the current session.");
    }
}
