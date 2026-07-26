using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using NUnit.Framework.Interfaces;
using SystemUptimeTracker.Qa.Automation.Infrastructure;
using SystemUptimeTracker.Qa.Automation.Pages;
using SystemUptimeTracker.Qa.Automation.Services;
using SystemUptimeTracker.Qa.Automation.Support;
using SystemUptimeTracker.Data.Identity;
using System.Text;
using System.Text.RegularExpressions;

namespace SystemUptimeTracker.Qa.Automation.TestBases;

public abstract class SystemUptimeTrackerPlaywrightTestBase : QaPlaywrightTestBase
{
    private readonly List<string> _consoleMessages = [];
    private readonly List<string> _pageErrors = [];
    private bool _pageDiagnosticsRegistered;

    protected sealed class AuthenticatedUiSession : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;

        public AuthenticatedUiSession(
            AsyncServiceScope scope,
            SystemUptimeTrackerHomePage homePage,
            string email,
            IReadOnlyList<string> assignedRoles)
        {
            _scope = scope;
            HomePage = homePage;
            Email = email;
            AssignedRoles = assignedRoles;
        }

        public SystemUptimeTrackerHomePage HomePage { get; }

        public string Email { get; }

        public IReadOnlyList<string> AssignedRoles { get; }

        public ValueTask DisposeAsync()
        {
            return _scope.DisposeAsync();
        }
    }

    protected override bool IncludeKeyVault => false;

    protected override string EnvironmentName => SystemUptimeTrackerTestEnvironment.Resolve();

    protected override string[] CreateHostArgs()
    {
        return SystemUptimeTrackerAppHostManager.CreateQaAutomationHostArgs();
    }

    protected override void OnBeforeHostCreated()
    {
        if (QaAutomationExecution.UseExternalHost)
        {
            return;
        }

        SystemUptimeTrackerAppHostManager.Acquire(SystemUptimeTrackerAppHostReadinessScope.SERVER_AND_CLIENT);
    }

    protected override void OnHostCreationFailed()
    {
        if (QaAutomationExecution.UseExternalHost)
        {
            return;
        }

        SystemUptimeTrackerAppHostManager.Release();
    }

    protected override async Task OnOneTimeSetUp()
    {
        await base.OnOneTimeSetUp();
        Logger.LogInformation(
            "QA automation Playwright test fixture starting for environment {EnvironmentName}.",
            EnvironmentName);

        await CreatePlaywrightSessionAsync();
        RegisterPageDiagnostics();
        Logger.LogInformation("Playwright page session created successfully for {EnvironmentName}.", EnvironmentName);
    }

    protected override async Task OnOneTimeTearDown()
    {
        await DisposePlaywrightSessionAsync();
        await base.OnOneTimeTearDown();
    }

    protected override async Task OnSetUp()
    {
        await base.OnSetUp();
        _consoleMessages.Clear();
        _pageErrors.Clear();
        Logger.LogInformation("Starting Playwright test {TestName}.", TestContext.CurrentContext.Test.Name);
        await Page.Context.ClearCookiesAsync();
        Logger.LogDebug("Cleared browser cookies before test {TestName}.", TestContext.CurrentContext.Test.Name);
    }

    protected override async Task OnTearDown()
    {
        try
        {
            if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
            {
                await CaptureFailureArtifactsAsync();
            }
        }
        finally
        {
            await base.OnTearDown();
        }
    }

    protected async Task<AuthenticatedUiSession> SignInWithProvisionedIdentityAsync()
    {
        Logger.LogInformation(
            "Provisioning and signing in with a scoped ephemeral identity for test {TestName}.",
            TestContext.CurrentContext.Test.Name);

        AsyncServiceScope scope = Services.CreateAsyncScope();

        try
        {
            ITestIdentityAccountProvisioningService provisioningService = scope.ServiceProvider
                .GetRequiredService<ITestIdentityAccountProvisioningService>();
            TestIdentityAccountProvisioningResult provisioningResult = await provisioningService.EnsureIndividualAccountReadyAsync();
            ISystemUptimeTrackerApiClient systemUptimeTrackerApiClient = scope.ServiceProvider
                .GetRequiredService<ISystemUptimeTrackerApiClient>();
            ApplicationDbContext applicationDbContext = scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();
            var credentials = new LoginCredentials
            {
                Username = provisioningResult.Email,
                Password = provisioningResult.Password
            };

            try
            {
                string accessToken = await systemUptimeTrackerApiClient.RequestLocalIdentityAccessTokenAsync(credentials);
                Logger.LogInformation(
                    "Verified direct API local identity token issuance for {Email}. AccessTokenLength={AccessTokenLength}.",
                    provisioningResult.Email,
                    accessToken.Length);
            }
            catch (Exception exception)
            {
                ApplicationUser? persistedUser = await applicationDbContext.Users
                    .SingleOrDefaultAsync(user => user.Email == provisioningResult.Email);

                throw new InvalidOperationException(
                    $"The provisioned QA identity account {provisioningResult.Email} could not obtain an API access token before the browser sign-in flow started. " +
                    $"PersistedUserFound={(persistedUser is not null)}; " +
                    $"PersistedUserName='{persistedUser?.UserName ?? "<null>"}'; " +
                    $"PersistedNormalizedUserName='{persistedUser?.NormalizedUserName ?? "<null>"}'; " +
                    $"PersistedEmailConfirmed={(persistedUser?.EmailConfirmed.ToString() ?? "<null>")}; " +
                    $"PersistedIsActive={(persistedUser?.IsActive.ToString() ?? "<null>")}; " +
                    $"PersistedAccessFailedCount={(persistedUser?.AccessFailedCount.ToString() ?? "<null>")}; " +
                    $"PersistedLockoutEnd='{persistedUser?.LockoutEnd?.ToString("O") ?? "<null>"}'.",
                    exception);
            }

            SystemUptimeTrackerHomePage homePage = CreatePage<SystemUptimeTrackerHomePage>();
            LoginPage loginPage = CreatePage<LoginPage>();
            await Page.GotoAsync(homePage.PageUrl);

            if (await loginPage.IsVisibleAsync())
            {
                Logger.LogInformation("Authentication page is visible; executing sign-in flow with a scoped ephemeral local identity account.");
                await loginPage.SignInAsync(provisioningResult.Email, provisioningResult.Password);
            }
            else
            {
                await homePage.WaitForLoadedAsync();

                if (!await homePage.HasSessionLinkAsync() || await homePage.HasPrimaryNavigationSignInLinkAsync())
                {
                    await homePage.OpenSignInAsync();
                    Logger.LogInformation("Authentication page is visible; executing sign-in flow with a scoped ephemeral local identity account.");
                    await loginPage.SignInAsync(provisioningResult.Email, provisioningResult.Password);
                }
                else
                {
                    Logger.LogInformation("Login page was not shown; continuing with the existing authenticated session.");
                }
            }

            try
            {
                await homePage.WaitForLoadedAsync();
            }
            catch (PlaywrightException exception)
            {
                string? loginError = await loginPage.GetErrorTextAsync();
                throw new InvalidOperationException(
                    $"The UI sign-in flow did not navigate back to the home page. CurrentUrl='{Page.Url}'. LoginError='{loginError ?? "<none>"}'.",
                    exception);
            }

            await homePage.WaitForSignedInAsync();

            return new AuthenticatedUiSession(
                scope,
                homePage,
                provisioningResult.Email,
                provisioningResult.AssignedRoles);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    protected override void OnAfterHostDisposed()
    {
        if (!QaAutomationExecution.UseExternalHost)
        {
            SystemUptimeTrackerAppHostManager.Release();
        }

        base.OnAfterHostDisposed();
    }

    private void RegisterPageDiagnostics()
    {
        if (_pageDiagnosticsRegistered)
        {
            return;
        }

        Page.Console += (_, message) =>
        {
            _consoleMessages.Add($"{DateTimeOffset.UtcNow:O} [{message.Type}] {message.Text}");
        };

        Page.PageError += (_, message) =>
        {
            _pageErrors.Add($"{DateTimeOffset.UtcNow:O} {message}");
        };

        _pageDiagnosticsRegistered = true;
    }

    private async Task CaptureFailureArtifactsAsync()
    {
        string artifactDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "TestResults",
            "qa-artifacts",
            SanitizePathSegment(TestContext.CurrentContext.Test.FullName),
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));

        Directory.CreateDirectory(artifactDirectory);

        string diagnosticsPath = Path.Combine(artifactDirectory, "diagnostics.txt");
        string screenshotPath = Path.Combine(artifactDirectory, "page.png");
        string pageMarkupPath = Path.Combine(artifactDirectory, "page.html");

        string pageTitle;
        try
        {
            pageTitle = await Page.TitleAsync();
        }
        catch (Exception exception)
        {
            pageTitle = $"<unavailable: {exception.GetType().Name}: {exception.Message}>";
        }

        string pageMarkup;
        try
        {
            pageMarkup = await Page.ContentAsync();
        }
        catch (Exception exception)
        {
            pageMarkup = $"<!-- Unable to capture page markup: {exception.GetType().Name}: {exception.Message} -->";
        }

        var diagnosticsBuilder = new StringBuilder();
        diagnosticsBuilder.AppendLine($"Test: {TestContext.CurrentContext.Test.FullName}");
        diagnosticsBuilder.AppendLine($"Outcome: {TestContext.CurrentContext.Result.Outcome}");
        diagnosticsBuilder.AppendLine($"CapturedUtc: {DateTimeOffset.UtcNow:O}");
        diagnosticsBuilder.AppendLine($"PageUrl: {Page.Url}");
        diagnosticsBuilder.AppendLine($"PageTitle: {pageTitle}");
        diagnosticsBuilder.AppendLine();
        diagnosticsBuilder.AppendLine("Recent console messages:");

        foreach (string message in _consoleMessages.TakeLast(20))
        {
            diagnosticsBuilder.AppendLine(message);
        }

        diagnosticsBuilder.AppendLine();
        diagnosticsBuilder.AppendLine("Recent page errors:");

        foreach (string message in _pageErrors.TakeLast(20))
        {
            diagnosticsBuilder.AppendLine(message);
        }

        await File.WriteAllTextAsync(diagnosticsPath, diagnosticsBuilder.ToString());
        await File.WriteAllTextAsync(pageMarkupPath, pageMarkup);

        try
        {
            await Page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = screenshotPath,
                FullPage = true
            });

            TestContext.AddTestAttachment(screenshotPath, "Playwright failure screenshot");
        }
        catch (Exception exception)
        {
            await File.AppendAllTextAsync(
                diagnosticsPath,
                $"{Environment.NewLine}Screenshot capture failed: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}");
        }

        TestContext.AddTestAttachment(diagnosticsPath, "Playwright failure diagnostics");
        TestContext.AddTestAttachment(pageMarkupPath, "Playwright rendered page markup");

        Logger.LogInformation(
            "Captured Playwright failure artifacts for {TestName} in {ArtifactDirectory}.",
            TestContext.CurrentContext.Test.Name,
            artifactDirectory);
    }

    private static string SanitizePathSegment(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (char character in value)
        {
            builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }
}
