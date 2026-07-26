using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SystemUptimeTracker.Qa.Automation.Configuration;
using SystemUptimeTracker.Qa.Automation.Support;
using System.Text.RegularExpressions;

namespace SystemUptimeTracker.Qa.Automation.Pages;

public sealed class LoginPage : SystemUptimeTrackerPageBase<LoginPage>
{
    public override string PageTitle { get; protected set; }
    public override string PageUrl { get; protected set; }

    public LoginPage(
        IPage page,
        IPageObjectFactory pageObjectFactory,
        ISystemUptimeTrackerPageCatalog pageCatalog,
        ILogger<LoginPage> logger)
        : base(page, pageObjectFactory, logger)
    {
        PageTitle = pageCatalog.GetPageTitle("Login", "Sign in");
        PageUrl = pageCatalog.GetPageUrl("Login", "https://localhost:3001/auth/login");
        LogResolvedPageConfiguration();
    }

    private ILocator EmailInput => Page.Locator("#auth-login-email").Or(Page.GetByRole(AriaRole.Textbox, new() { NameRegex = new Regex("email|phone|skype|username", RegexOptions.IgnoreCase) }));

    private ILocator PasswordInput => Page.Locator("#auth-login-password").Or(Page.GetByRole(AriaRole.Textbox, new() { NameRegex = new Regex("password", RegexOptions.IgnoreCase) }));

    private ILocator NextButton => Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("next|submit", RegexOptions.IgnoreCase) });

    private ILocator SignInButton => Page.Locator("#auth-login-submit").Or(Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("sign in|verify|submit", RegexOptions.IgnoreCase) }));

    private ILocator ErrorAlert => Page.Locator("#auth-login-error").Or(Page.GetByRole(AriaRole.Alert));

    private ILocator ReturnToInput => Page.Locator("input[name='returnTo']");

    private ILocator StaySignedInNoButton => Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^no$", RegexOptions.IgnoreCase) });

    public async Task<string?> GetReturnToValueAsync()
    {
        return await ReturnToInput.GetAttributeAsync("value");
    }

    public async Task<string?> GetErrorTextAsync()
    {
        if (await ErrorAlert.CountAsync() == 0)
        {
            return null;
        }

        return (await ErrorAlert.InnerTextAsync()).Trim();
    }

    public async Task<bool> IsVisibleAsync()
    {
        bool isVisible = await TryWaitForVisibleAsync(EmailInput) || await TryWaitForVisibleAsync(PasswordInput);
        LogObservation("Login page visibility evaluated to {IsVisible} at {CurrentUrl}.", isVisible, Page.Url);
        return isVisible;
    }

    public async Task WaitForLoadedAsync(string? expectedReturnTo = null)
    {
        await Assertions.Expect(Page).ToHaveTitleAsync(new Regex("^Sign in$", RegexOptions.IgnoreCase));
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/auth/login", RegexOptions.IgnoreCase));
        await Assertions.Expect(EmailInput).ToBeVisibleAsync();
        await Assertions.Expect(PasswordInput).ToBeVisibleAsync();
        await Assertions.Expect(SignInButton).ToBeVisibleAsync();

        if (!string.IsNullOrWhiteSpace(expectedReturnTo))
        {
            await Assertions.Expect(ReturnToInput).ToHaveValueAsync(expectedReturnTo);
        }

        LogPageLoaded("Verified the local identity sign-in page is displayed.");
    }

    public async Task SignInAsync(string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        LogObservation("Starting local sign-in flow using scoped ephemeral credentials.");

        if (await EmailInput.CountAsync() > 0)
        {
            LogAction("EnterUsername", "EmailInput");
            await EmailInput.FillAsync(username);

            if (await NextButton.CountAsync() > 0)
            {
                await NextButton.ClickAsync();
            }
        }

        LogAction("EnterPassword", "PasswordInput");
        await PasswordInput.FillAsync(password);
        await SignInButton.ClickAsync();

        if (await StaySignedInNoButton.CountAsync() > 0)
        {
            LogAction("DeclineStaySignedIn", "StaySignedInPrompt");
            await StaySignedInNoButton.ClickAsync();
        }

        LogObservation("Completed local sign-in submission flow.");
    }
}
