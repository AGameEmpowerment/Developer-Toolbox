using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SystemUptimeTracker.Qa.Automation.Configuration;
using SystemUptimeTracker.Qa.Automation.Support;
using System.Text.RegularExpressions;

namespace SystemUptimeTracker.Qa.Automation.Pages;

public sealed class SystemUptimeTrackerAdminUsersPage : SystemUptimeTrackerPageBase<SystemUptimeTrackerAdminUsersPage>
{
    public override string PageTitle { get; protected set; }

    public override string PageUrl { get; protected set; }

    public SystemUptimeTrackerAdminUsersPage(
        IPage page,
        IPageObjectFactory pageObjectFactory,
        ISystemUptimeTrackerPageCatalog pageCatalog,
        ILogger<SystemUptimeTrackerAdminUsersPage> logger)
        : base(page, pageObjectFactory, logger)
    {
        PageTitle = pageCatalog.GetPageTitle("AdminUsers", "Admin Users");
        PageUrl = pageCatalog.GetPageUrl("AdminUsers", "https://localhost:3001/admin/users");
        LogResolvedPageConfiguration();
    }

    private ILocator PageRoot => Page.Locator("#admin-users-page");

    private ILocator Heading => Page.Locator("#admin-users-page-title");

    private ILocator UsersTable => Page.Locator("[data-testid='admin-users-page-table']");

    public async Task WaitForLoadedAsync()
    {
        await ExpectVisibleAsync(PageRoot);
        await Assertions.Expect(Heading).ToHaveTextAsync("Users");
        await ExpectVisibleAsync(UsersTable);
        await Assertions.Expect(Page).ToHaveTitleAsync(new Regex("^Admin Users\\s*\\|\\s*System Uptime Tracker$", RegexOptions.IgnoreCase));
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/admin/users$", RegexOptions.IgnoreCase));
        LogPageLoaded("Verified the admin user-management surface.");
    }

    public async Task WaitForUserRowAsync(string email, string displayName)
    {
        ILocator row = UserRow(email);
        await Assertions.Expect(row).ToContainTextAsync(email);

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            await Assertions.Expect(row).ToContainTextAsync(displayName);
        }

        LogObservation("Verified admin user row for {Email}.", email);
    }

    public async Task WaitForUserStatusAsync(string email, string status)
    {
        await Assertions.Expect(UserRow(email)).ToContainTextAsync(status);
        LogObservation("Verified status {Status} for admin user row {Email}.", status, email);
    }

    public async Task WaitForRoleCheckedAsync(string email, string role, bool expectedChecked)
    {
        ILocator roleCheckbox = RoleCheckbox(email, role);

        if (expectedChecked)
        {
            await Assertions.Expect(roleCheckbox).ToBeCheckedAsync();
        }
        else
        {
            await Assertions.Expect(roleCheckbox).Not.ToBeCheckedAsync();
        }

        LogObservation("Verified role {Role} checked state {ExpectedChecked} for {Email}.", role, expectedChecked, email);
    }

    public async Task SetRoleAsync(string email, string role, bool shouldBeChecked)
    {
        ILocator roleCheckbox = RoleCheckbox(email, role);
        LogAction(shouldBeChecked ? "CheckRole" : "UncheckRole", $"{email}:{role}");
        await roleCheckbox.SetCheckedAsync(shouldBeChecked);
    }

    public async Task SaveRolesAsync(string userButtonName)
    {
        LogAction("SaveRoles", userButtonName);
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Save roles for {userButtonName}" }).ClickAsync();
        await ExpectVisibleAsync(PageRoot);
    }

    public async Task ReloadAndWaitForLoadedAsync()
    {
        await Page.ReloadAsync();
        await WaitForLoadedAsync();
    }

    public async Task<int?> NavigateAndReturnStatusAsync()
    {
        IResponse? response = await Page.GotoAsync(PageUrl);
        return response?.Status;
    }

    public async Task WaitForNotRenderedAsync()
    {
        await Assertions.Expect(PageRoot).ToHaveCountAsync(0);
        LogObservation("Verified the admin user-management surface is not rendered for the current session.");
    }

    private ILocator UserRow(string email)
    {
        return UsersTable.GetByRole(AriaRole.Row).Filter(new()
        {
            HasText = email
        });
    }

    private ILocator RoleCheckbox(string email, string role)
    {
        return UserRow(email).GetByRole(AriaRole.Checkbox, new() { Name = role });
    }
}
