using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SystemUptimeTracker.Qa.Automation.Pages;
using SystemUptimeTracker.Qa.Automation.Services;
using SystemUptimeTracker.Qa.Automation.TestBases;

namespace SystemUptimeTracker.Qa.Automation.Ui;

[TestFixture(Category = "Automation"), Category("Integration"), Category("Ui"), Category("Authenticated")]
public sealed class SystemUptimeTrackerAuthenticatedUiTests : SystemUptimeTrackerPlaywrightTestBase
{
    [Test, Category("Functional")]
    public async Task HomePage_WithProvisionedIdentity_LoadsSignedInLandingPage()
    {
        Logger.LogInformation("Starting authenticated landing page validation.");

        await using AuthenticatedUiSession session = await SignInWithProvisionedIdentityAsync();
        SystemUptimeTrackerHomePage homePage = session.HomePage;

        string headingText = await homePage.GetHeadingTextAsync();
        string userDisplayName = await homePage.GetUserDisplayNameAsync();

        Assert.Multiple(() =>
        {
            Assert.That(headingText, Is.Not.Empty);
            Assert.That(userDisplayName, Is.Not.Empty);
            Assert.That(session.AssignedRoles, Is.Not.Empty);
        });

        Logger.LogInformation("Authenticated landing page validation completed successfully.");
    }

    [Test, Category("Functional")]
    public async Task HomePage_AdminIdentity_OpensAdminUsersPage()
    {
        Logger.LogInformation("Starting authenticated admin navigation validation.");

        await using AuthenticatedUiSession session = await SignInWithProvisionedIdentityAsync();
        SystemUptimeTrackerHomePage homePage = session.HomePage;
        SystemUptimeTrackerAdminUsersPage adminUsersPage = CreatePage<SystemUptimeTrackerAdminUsersPage>();

        Assert.That(session.AssignedRoles, Contains.Item("Admin"));

        await homePage.OpenAdminUsersFromPrimaryNavigationAsync();
        await adminUsersPage.WaitForLoadedAsync();

        Logger.LogInformation("Authenticated admin navigation validation completed successfully.");
    }

    [Test, Category("Functional")]
    public async Task AuthenticatedRoutes_DoNotRenderUnexpected404Pages()
    {
        Logger.LogInformation("Starting authenticated route 404 regression validation.");

        await using AuthenticatedUiSession session = await SignInWithProvisionedIdentityAsync();
        SystemUptimeTrackerHomePage homePage = session.HomePage;
        SystemUptimeTrackerAdminUsersPage adminUsersPage = CreatePage<SystemUptimeTrackerAdminUsersPage>();

        Assert.That(session.AssignedRoles, Contains.Item("Admin"));

        await AssertRouteDoesNotReturn404Async(homePage.PageUrl);
        await homePage.WaitForLoadedAsync();

        await AssertRouteDoesNotReturn404Async(adminUsersPage.PageUrl);
        await adminUsersPage.WaitForLoadedAsync();

        Logger.LogInformation("Authenticated route 404 regression validation completed successfully.");
    }

    [Test, Category("Functional")]
    public async Task AdminUsersPage_AdminIdentity_AssignsContributorRoleAndBlocksContributorAdminAccess()
    {
        Logger.LogInformation("Starting admin user-management role assignment browser validation.");

        string managedDisplayName = $"QA Managed {Guid.NewGuid():N}"[..18];

        await using AsyncServiceScope managedUserScope = Services.CreateAsyncScope();
        ITestIdentityAccountProvisioningService provisioningService = managedUserScope.ServiceProvider
            .GetRequiredService<ITestIdentityAccountProvisioningService>();
        TestIdentityAccountProvisioningResult managedUser = await provisioningService.EnsureIndividualAccountReadyWithRolesAsync(
            [],
            managedDisplayName);

        await using AuthenticatedUiSession adminSession = await SignInWithProvisionedIdentityAsync();
        SystemUptimeTrackerAdminUsersPage adminUsersPage = CreatePage<SystemUptimeTrackerAdminUsersPage>();

        Assert.Multiple(() =>
        {
            Assert.That(adminSession.AssignedRoles, Contains.Item("Admin"));
            Assert.That(managedUser.AssignedRoles, Is.Empty);
        });

        await adminUsersPage.NavigateToAsync();
        await adminUsersPage.WaitForLoadedAsync();
        await adminUsersPage.WaitForUserRowAsync(adminSession.Email, string.Empty);
        await adminUsersPage.WaitForUserRowAsync(managedUser.Email, managedDisplayName);
        await adminUsersPage.WaitForUserStatusAsync(managedUser.Email, "Active");
        await adminUsersPage.WaitForUserStatusAsync(managedUser.Email, "Pending");
        await adminUsersPage.WaitForRoleCheckedAsync(managedUser.Email, "Contributor", expectedChecked: false);

        await adminUsersPage.SetRoleAsync(managedUser.Email, "Contributor", shouldBeChecked: true);
        await adminUsersPage.SaveRolesAsync(managedDisplayName);
        await adminUsersPage.WaitForUserStatusAsync(managedUser.Email, "Approved");
        await adminUsersPage.ReloadAndWaitForLoadedAsync();
        await adminUsersPage.WaitForUserStatusAsync(managedUser.Email, "Approved");
        await adminUsersPage.WaitForRoleCheckedAsync(managedUser.Email, "Contributor", expectedChecked: true);
        await adminUsersPage.WaitForRoleCheckedAsync(managedUser.Email, "Admin", expectedChecked: false);

        await Page.Context.ClearCookiesAsync();

        SystemUptimeTrackerHomePage contributorHomePage = CreatePage<SystemUptimeTrackerHomePage>();
        LoginPage loginPage = CreatePage<LoginPage>();
        await Page.GotoAsync(contributorHomePage.PageUrl);
        await loginPage.WaitForLoadedAsync("/");
        await loginPage.SignInAsync(managedUser.Email, managedUser.Password);

        await contributorHomePage.WaitForLoadedAsync();
        await contributorHomePage.WaitForSignedInAsync();
        await contributorHomePage.WaitForAdminNavigationHiddenAsync();

        int? adminRouteStatus = await adminUsersPage.NavigateAndReturnStatusAsync();
        await adminUsersPage.WaitForNotRenderedAsync();

        Assert.That(adminRouteStatus, Is.EqualTo(404));

        Logger.LogInformation("Admin user-management role assignment browser validation completed successfully.");
    }

    private async Task AssertRouteDoesNotReturn404Async(string url)
    {
        IResponse? response = await Page.GotoAsync(url);

        Assert.That(response?.Status, Is.Not.EqualTo(404), $"Expected route '{url}' not to return HTTP 404.");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = "Error code: 404" })).ToHaveCountAsync(0);
    }
}
