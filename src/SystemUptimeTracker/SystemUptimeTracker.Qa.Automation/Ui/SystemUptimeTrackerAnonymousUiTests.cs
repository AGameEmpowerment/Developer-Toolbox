using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Data.Identity;
using SystemUptimeTracker.Qa.Automation.Pages;
using SystemUptimeTracker.Qa.Automation.TestBases;
using System.Text.RegularExpressions;

namespace SystemUptimeTracker.Qa.Automation.Ui;

[TestFixture(Category = "Automation"), Category("Integration"), Category("Ui")]
public sealed class SystemUptimeTrackerAnonymousUiTests : SystemUptimeTrackerPlaywrightTestBase
{
    private const string ANONYMOUS_TEST_ADMIN_EMAIL = "anonymous-test-admin@example.invalid";

    protected override async Task OnOneTimeSetUp()
    {
        await base.OnOneTimeSetUp();

        // Ensure at least one active administrator exists in the database so the
        // application follows the normal anonymous-to-login path instead of the
        // first-time setup route during these QA checks.
        //
        // We create a user with an email that does NOT match the QA test pattern
        // (systemuptimetracker-qa+*@example.invalid) so it won't be deleted by the
        // TestIdentityAccountCleanupService during OneTimeSetUp cleanup.
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Check if the user already exists
        ApplicationUser? existingUser = await userManager.FindByEmailAsync(ANONYMOUS_TEST_ADMIN_EMAIL);
        if (existingUser is null)
        {
            var adminUser = new ApplicationUser
            {
                UserName = ANONYMOUS_TEST_ADMIN_EMAIL,
                Email = ANONYMOUS_TEST_ADMIN_EMAIL,
                EmailConfirmed = true,
                DisplayName = "Anonymous Test Administrator",
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            IdentityResult createResult = await userManager.CreateAsync(adminUser, "TestPassword123!");
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create anonymous test admin user: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
            }

            // Ensure all application roles exist
            foreach (string roleName in ApplicationRoleNames.All)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // Assign any missing roles to the admin user
            IList<string> currentRoles = await userManager.GetRolesAsync(adminUser);
            string[] missingRoles = ApplicationRoleNames.All
                .Except(currentRoles, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (missingRoles.Length > 0)
            {
                IdentityResult rolesResult = await userManager.AddToRolesAsync(adminUser, missingRoles);
                if (!rolesResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to assign roles to anonymous test admin user: {string.Join(", ", rolesResult.Errors.Select(e => e.Description))}");
                }
            }

            Logger.LogInformation("Created anonymous test admin user '{Email}' for anonymous UI tests.", ANONYMOUS_TEST_ADMIN_EMAIL);
        }
        else
        {
            Logger.LogInformation("Anonymous test admin user '{Email}' already exists.", ANONYMOUS_TEST_ADMIN_EMAIL);
        }
    }

    protected override async Task OnOneTimeTearDown()
    {
        try
        {
            // Clean up the anonymous test admin user
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            ApplicationUser? adminUser = await userManager.FindByEmailAsync(ANONYMOUS_TEST_ADMIN_EMAIL);
            if (adminUser is not null)
            {
                await userManager.DeleteAsync(adminUser);
                Logger.LogInformation("Deleted anonymous test admin user '{Email}'.", ANONYMOUS_TEST_ADMIN_EMAIL);
            }
        }
        finally
        {
            await base.OnOneTimeTearDown();
        }
    }

    [Test, Category("Functional")]
    public async Task AnonymousAndLoginRoutes_DoNotRenderUnexpected404Pages()
    {
        Logger.LogInformation("Starting anonymous route 404 regression validation.");

        SystemUptimeTrackerHomePage homePage = CreatePage<SystemUptimeTrackerHomePage>();
        LoginPage loginPage = CreatePage<LoginPage>();
        string baseUrl = homePage.PageUrl.TrimEnd('/');

        await AssertRouteDoesNotReturn404Async(loginPage.PageUrl);
        await loginPage.WaitForLoadedAsync();

        await AssertRouteDoesNotReturn404Async($"{baseUrl}/login");
        await Assertions.Expect(Page).ToHaveTitleAsync(new Regex("^Login\\s*\\|\\s*System Uptime Tracker$", RegexOptions.IgnoreCase));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = "Login" })).ToBeVisibleAsync();

        foreach (string protectedRoute in new[] { "/", "/admin/users" })
        {
            string targetUrl = protectedRoute == "/" ? homePage.PageUrl : $"{baseUrl}{protectedRoute}";
            await AssertRouteDoesNotReturn404Async(targetUrl);
            await loginPage.WaitForLoadedAsync(protectedRoute);
        }

        Logger.LogInformation("Anonymous route 404 regression validation completed successfully.");
    }

    [Test, Category("Functional")]
    public async Task RootRequest_AnonymousUser_IsRedirectedToSignIn()
    {
        Logger.LogInformation("Starting anonymous root login-gate validation.");

        SystemUptimeTrackerHomePage homePage = CreatePage<SystemUptimeTrackerHomePage>();
        LoginPage loginPage = CreatePage<LoginPage>();

        await Page.GotoAsync(homePage.PageUrl);
        await loginPage.WaitForLoadedAsync("/");

        string? returnTo = await loginPage.GetReturnToValueAsync();

        Assert.That(returnTo, Is.EqualTo("/"));

        Logger.LogInformation("Anonymous root login-gate validation completed successfully.");
    }

    [Test, Category("Functional")]
    public async Task AdminRoute_AnonymousUser_IsRedirectedToSignIn()
    {
        Logger.LogInformation("Starting anonymous admin-route login-gate validation.");

        SystemUptimeTrackerHomePage homePage = CreatePage<SystemUptimeTrackerHomePage>();
        LoginPage loginPage = CreatePage<LoginPage>();

        await Page.GotoAsync($"{homePage.PageUrl.TrimEnd('/')}/admin/users");
        await loginPage.WaitForLoadedAsync("/admin/users");

        string? returnTo = await loginPage.GetReturnToValueAsync();
        Assert.That(returnTo, Is.EqualTo("/admin/users"));

        Logger.LogInformation("Anonymous admin-route login-gate validation completed successfully.");
    }

    private async Task AssertRouteDoesNotReturn404Async(string url)
    {
        IResponse? response = await Page.GotoAsync(url);

        Assert.That(response?.Status, Is.Not.EqualTo(404), $"Expected route '{url}' not to return HTTP 404.");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = "Error code: 404" })).ToHaveCountAsync(0);
    }
}
