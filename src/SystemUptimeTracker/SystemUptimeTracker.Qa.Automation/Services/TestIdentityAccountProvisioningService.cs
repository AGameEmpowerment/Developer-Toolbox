using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemUptimeTracker.Data.Identity;
using SystemUptimeTracker.Qa.Automation.Support;
using System.Security.Cryptography;

namespace SystemUptimeTracker.Qa.Automation.Services;

public sealed class TestIdentityAccountProvisioningService : ITestIdentityAccountProvisioningService
{
    private string? _disposableAccountEmail;
    private string? _provisioningPassword;
    private bool _disposed;

    private readonly AutomationAppSettings _appSettings;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<TestIdentityAccountProvisioningService> _logger;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public TestIdentityAccountProvisioningService(
        IOptions<AutomationAppSettings> appSettings,
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<TestIdentityAccountProvisioningService> logger)
    {
        _appSettings = appSettings.Value;
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    public async Task<TestIdentityAccountProvisioningResult> EnsureIndividualAccountReadyAsync(CancellationToken cancellationToken = default)
    {
        LocalIdentityTestUserConfiguration settings = _appSettings.LocalIdentityTestUser;
        return await EnsureIndividualAccountReadyCoreAsync(
            settings,
            settings.RequiredRoles,
            displayName: null,
            includeExistingRoles: true,
            useConfiguredRoleDefault: true,
            cancellationToken);
    }

    public async Task<TestIdentityAccountProvisioningResult> EnsureIndividualAccountReadyWithRolesAsync(
        IReadOnlyCollection<string> requiredRoles,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requiredRoles);

        LocalIdentityTestUserConfiguration settings = _appSettings.LocalIdentityTestUser;
        return await EnsureIndividualAccountReadyCoreAsync(
            settings,
            requiredRoles,
            displayName,
            includeExistingRoles: false,
            useConfiguredRoleDefault: false,
            cancellationToken);
    }

    private async Task<TestIdentityAccountProvisioningResult> EnsureIndividualAccountReadyCoreAsync(
        LocalIdentityTestUserConfiguration settings,
        IEnumerable<string> requestedRoles,
        string? displayName,
        bool includeExistingRoles,
        bool useConfiguredRoleDefault,
        CancellationToken cancellationToken)
    {
        string email = ResolveProvisioningEmail(settings);
        string password = ResolveProvisioningPassword();

        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        _logger.LogInformation("Ensuring local individual-account identity exists for {Email}.", email);

        await EnsureDatabaseReadyAsync(cancellationToken);

        ApplicationUser? user = await _userManager.FindByEmailAsync(email);
        bool userCreated = false;
        bool passwordReset = false;

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = displayName?.Trim() ?? string.Empty,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            IdentityResult createResult = await _userManager.CreateAsync(user, password);
            EnsureSucceeded(createResult, "create local test user account");
            userCreated = true;
            _logger.LogInformation("Created local test identity account for {Email}.", email);
        }
        else
        {
            passwordReset = await EnsurePasswordMatchesAsync(user, password);
            await EnsureUserProfileMatchesAsync(user, email, displayName);
        }

        if (!user.EmailConfirmed)
        {
            string confirmationToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            IdentityResult confirmResult = await _userManager.ConfirmEmailAsync(user, confirmationToken);
            EnsureSucceeded(confirmResult, "confirm local test user email");
            _logger.LogInformation("Confirmed email for local test identity account {Email}.", email);
        }

        IReadOnlyList<string> requiredRoles = await ResolveRequiredRolesAsync(
            requestedRoles,
            includeExistingRoles,
            useConfiguredRoleDefault,
            cancellationToken);
        await EnsureRolesExistAsync(requiredRoles);
        await EnsureUserHasRequiredRolesAsync(user, requiredRoles, removeUnrequestedRoles: !includeExistingRoles);

        await _userManager.SetLockoutEndDateAsync(user, null);
        SignInResult signInResult = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
        bool signInValidated = signInResult.Succeeded;

        if (!signInValidated)
        {
            throw new InvalidOperationException($"The local test identity account {email} could not sign in. Identity result: {signInResult}.");
        }

        IReadOnlyList<string> assignedRoles = (await _userManager.GetRolesAsync(user)).ToArray();

        _logger.LogInformation(
            "Local test identity account {Email} is ready. Created={UserCreated}; PasswordReset={PasswordReset}; EmailConfirmed={EmailConfirmed}; Roles={AssignedRoleCount}.",
            email,
            userCreated,
            passwordReset,
            user.EmailConfirmed,
            assignedRoles.Count);

        return new TestIdentityAccountProvisioningResult(
            Email: email,
            Password: password,
            UserCreated: userCreated,
            PasswordReset: passwordReset,
            EmailConfirmed: user.EmailConfirmed,
            SignInValidated: signInValidated,
            CleanupScheduled: ShouldCleanupProvisionedAccount(),
            RequiredRoles: requiredRoles,
            AssignedRoles: assignedRoles.OrderBy(role => role, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!ShouldCleanupProvisionedAccount() || string.IsNullOrWhiteSpace(_disposableAccountEmail))
        {
            return;
        }

        _dbContext.ChangeTracker.Clear();

        ApplicationUser? user = await _userManager.FindByEmailAsync(_disposableAccountEmail);
        if (user is null)
        {
            _logger.LogDebug("Disposable local test identity account {Email} was already removed.", _disposableAccountEmail);
            return;
        }

        IdentityResult deleteResult = await _userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded && deleteResult.Errors.Any(error => string.Equals(error.Code, nameof(IdentityErrorDescriber.ConcurrencyFailure), StringComparison.Ordinal)))
        {
            _logger.LogWarning(
                "Retrying deletion for disposable local test identity account {Email} after a concurrency failure.",
                _disposableAccountEmail);

            _dbContext.ChangeTracker.Clear();
            user = await _userManager.FindByEmailAsync(_disposableAccountEmail);

            if (user is null)
            {
                _logger.LogDebug("Disposable local test identity account {Email} was removed before the retry completed.", _disposableAccountEmail);
                return;
            }

            deleteResult = await _userManager.DeleteAsync(user);
        }

        EnsureSucceeded(deleteResult, $"delete disposable local test user '{_disposableAccountEmail}'");
        _logger.LogInformation("Deleted disposable local test identity account {Email} during scope cleanup.", _disposableAccountEmail);
    }

    private async Task EnsureDatabaseReadyAsync(CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsRelational())
        {
            try
            {
                await _dbContext.Database.MigrateAsync(cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 2714)
            {
                _logger.LogWarning(
                    exception,
                    "Skipping QA identity migration replay because the identity schema already exists in the runtime database.");
            }

            return;
        }

        await _dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    private string ResolveProvisioningEmail(LocalIdentityTestUserConfiguration settings)
    {
        if (!string.IsNullOrWhiteSpace(_disposableAccountEmail))
        {
            return _disposableAccountEmail;
        }

        string localPartPrefix = string.IsNullOrWhiteSpace(settings.EmailLocalPartPrefix)
            ? "systemuptimetracker-qa"
            : settings.EmailLocalPartPrefix.Trim();

        string emailDomain = string.IsNullOrWhiteSpace(settings.EmailDomain)
            ? "example.invalid"
            : settings.EmailDomain.Trim().TrimStart('@');

        _disposableAccountEmail = BuildDisposableEmail(localPartPrefix, emailDomain);
        return _disposableAccountEmail;
    }

    private string ResolveProvisioningPassword()
    {
        if (!string.IsNullOrWhiteSpace(_provisioningPassword))
        {
            return _provisioningPassword;
        }

        _provisioningPassword = BuildDisposablePassword();
        return _provisioningPassword;
    }

    private bool ShouldCleanupProvisionedAccount()
    {
        return _dbContext.Database.IsRelational();
    }

    private static string BuildDisposableEmail(string localPartPrefix, string emailDomain)
    {
        return $"{localPartPrefix}+{Guid.NewGuid():N}@{emailDomain}";
    }

    private static string BuildDisposablePassword()
    {
        return $"Qa!{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}z9";
    }

    private async Task<bool> EnsurePasswordMatchesAsync(ApplicationUser user, string password)
    {
        SignInResult signInResult = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
        if (signInResult.Succeeded)
        {
            return false;
        }

        string resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        IdentityResult resetResult = await _userManager.ResetPasswordAsync(user, resetToken, password);
        EnsureSucceeded(resetResult, "reset local test user password");
        _logger.LogInformation("Reset the password for local test identity account {Email}.", user.Email);
        return true;
    }

    private async Task EnsureUserProfileMatchesAsync(ApplicationUser user, string email, string? displayName)
    {
        bool requiresUpdate = false;

        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = email;
            requiresUpdate = true;
        }

        if (!string.Equals(user.UserName, email, StringComparison.OrdinalIgnoreCase))
        {
            user.UserName = email;
            requiresUpdate = true;
        }

        if (displayName is not null && !string.Equals(user.DisplayName, displayName.Trim(), StringComparison.Ordinal))
        {
            user.DisplayName = displayName.Trim();
            requiresUpdate = true;
        }

        if (!requiresUpdate)
        {
            return;
        }

        IdentityResult updateResult = await _userManager.UpdateAsync(user);
        EnsureSucceeded(updateResult, "update local test user profile");
    }

    private async Task<IReadOnlyList<string>> ResolveRequiredRolesAsync(
        IEnumerable<string> configuredRoles,
        bool includeExistingRoles,
        bool useConfiguredRoleDefault,
        CancellationToken cancellationToken)
    {
        List<string> existingRoles = includeExistingRoles
            ? await _roleManager.Roles
                .Select(role => role.Name)
                .Where(roleName => !string.IsNullOrWhiteSpace(roleName))
                .Cast<string>()
                .ToListAsync(cancellationToken)
            : [];

        string[] configuredRoleArray = configuredRoles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .ToArray();

        if (useConfiguredRoleDefault && configuredRoleArray.Length == 0)
        {
            configuredRoleArray = ["Admin"];
        }

        return existingRoles
            .Concat(configuredRoleArray)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task EnsureRolesExistAsync(IEnumerable<string> roles)
    {
        foreach (string role in roles)
        {
            if (await _roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            IdentityResult createRoleResult = await _roleManager.CreateAsync(new IdentityRole(role));
            EnsureSucceeded(createRoleResult, $"create role '{role}'");
            _logger.LogInformation("Created missing identity role {RoleName} for QA automation.", role);
        }
    }

    private async Task EnsureUserHasRequiredRolesAsync(
        ApplicationUser user,
        IReadOnlyList<string> requiredRoles,
        bool removeUnrequestedRoles)
    {
        IList<string> assignedRoles = await _userManager.GetRolesAsync(user);

        if (removeUnrequestedRoles)
        {
            string[] rolesToRemove = assignedRoles
                .Except(requiredRoles, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (rolesToRemove.Length > 0)
            {
                IdentityResult removeRolesResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                EnsureSucceeded(removeRolesResult, $"remove roles from user '{user.Email}'");
                _logger.LogInformation("Removed {RoleCount} role(s) from local test identity account {Email}.", rolesToRemove.Length, user.Email);
            }
        }

        string[] missingRoles = requiredRoles
            .Except(assignedRoles, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingRoles.Length == 0)
        {
            return;
        }

        IdentityResult addToRolesResult = await _userManager.AddToRolesAsync(user, missingRoles);
        EnsureSucceeded(addToRolesResult, $"assign roles to user '{user.Email}'");
        _logger.LogInformation("Assigned {RoleCount} roles to local test identity account {Email}.", missingRoles.Length, user.Email);
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        string message = string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
        throw new InvalidOperationException($"Failed to {operation}. {message}");
    }
}
