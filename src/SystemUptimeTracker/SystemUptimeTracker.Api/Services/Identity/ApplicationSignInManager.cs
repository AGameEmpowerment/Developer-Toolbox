using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SystemUptimeTracker.Data.Identity;

namespace SystemUptimeTracker.Api.Services.Identity;

public sealed class ApplicationSignInManager : SignInManager<ApplicationUser>
{
    public ApplicationSignInManager(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
    }

    public override async Task<bool> CanSignInAsync(ApplicationUser user)
    {
        if (!user.IsActive)
        {
            Logger.LogInformation("Blocked sign-in for inactive user {UserId}.", user.Id);
            return false;
        }

        return await base.CanSignInAsync(user);
    }

    public override async Task<SignInResult> PasswordSignInAsync(
        string userName,
        string password,
        bool isPersistent,
        bool lockoutOnFailure)
    {
        SignInResult result = await base.PasswordSignInAsync(userName, password, isPersistent, lockoutOnFailure);

        if (!result.Succeeded)
        {
            return result;
        }

        ApplicationUser? user = await UserManager.FindByNameAsync(userName)
            ?? await UserManager.FindByEmailAsync(userName);

        if (user is not null)
        {
            await RecordSuccessfulSignInAsync(user);
        }

        return result;
    }

    private async Task RecordSuccessfulSignInAsync(ApplicationUser user)
    {
        user.LastLoginAtUtc = DateTimeOffset.UtcNow;

        IdentityResult updateResult = await UserManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            Logger.LogWarning(
                "Failed to persist last login timestamp for user {UserId}: {Errors}",
                user.Id,
                string.Join("; ", updateResult.Errors.Select(error => error.Description)));
        }
    }
}