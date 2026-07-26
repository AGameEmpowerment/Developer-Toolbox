using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Data.Identity;

namespace SystemUptimeTracker.Api.Services.Identity;

public sealed class ApplicationUserManager : UserManager<ApplicationUser>
{
    private static readonly SemaphoreSlim _createUserGate = new(1, 1);
    private static readonly AsyncLocal<bool> _bypassCustomCreate = new();

    private readonly IIdentityRoleSeeder _identityRoleSeeder;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<ApplicationUserManager> _logger;

    public ApplicationUserManager(
        IUserStore<ApplicationUser> store,
        IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IEnumerable<IUserValidator<ApplicationUser>> userValidators,
        IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<ApplicationUserManager> logger,
        IIdentityRoleSeeder identityRoleSeeder,
        IHostEnvironment hostEnvironment)
        : base(
            store,
            optionsAccessor,
            passwordHasher,
            userValidators,
            passwordValidators,
            keyNormalizer,
            errors,
            services,
            logger)
    {
        _identityRoleSeeder = identityRoleSeeder;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public override Task<IdentityResult> CreateAsync(ApplicationUser user)
    {
        if (_bypassCustomCreate.Value)
        {
            return base.CreateAsync(user);
        }

        return CreateCoreAsync(user, () => base.CreateAsync(user));
    }

    public override Task<IdentityResult> CreateAsync(ApplicationUser user, string password)
    {
        if (_bypassCustomCreate.Value)
        {
            return base.CreateAsync(user, password);
        }

        return CreateCoreAsync(user, async () =>
        {
            _bypassCustomCreate.Value = true;

            try
            {
                return await base.CreateAsync(user, password);
            }
            finally
            {
                _bypassCustomCreate.Value = false;
            }
        });
    }

    private async Task<IdentityResult> CreateCoreAsync(ApplicationUser user, Func<Task<IdentityResult>> createUser)
    {
        user.CreatedAtUtc = user.CreatedAtUtc == default ? DateTimeOffset.UtcNow : user.CreatedAtUtc;
        user.DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? ResolveDefaultDisplayName(user)
            : user.DisplayName.Trim();

        await _createUserGate.WaitAsync();

        try
        {
            await _identityRoleSeeder.EnsureSeedDataAsync();

            bool hadExistingUsers = await Users.AnyAsync();
            IdentityResult createResult = await createUser();

            if (!createResult.Succeeded)
            {
                return createResult;
            }

            if (!hadExistingUsers && CanBootstrapFirstUser())
            {
                // Local/test convenience only. Production first-run admin creation
                // must go through the controlled bootstrap endpoints.
                await BootstrapFirstUserIfNeededAsync(user);
            }

            return createResult;
        }
        finally
        {
            _createUserGate.Release();
        }
    }

    private async Task BootstrapFirstUserIfNeededAsync(ApplicationUser user)
    {
        int userCount = await Users.CountAsync();
        if (userCount != 1)
        {
            return;
        }

        IList<string> assignedRoles = await GetRolesAsync(user);
        string[] missingRoles = ApplicationRoleNames.All
            .Except(assignedRoles, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingRoles.Length == 0)
        {
            return;
        }

        IdentityResult addRolesResult = await AddToRolesAsync(user, missingRoles);
        if (!addRolesResult.Succeeded)
        {
            throw new InvalidOperationException($"Failed to bootstrap the first local user '{user.Email}'. {FormatErrors(addRolesResult)}");
        }

        _logger.LogInformation(
            "Bootstrapped first local identity user {Email} with {RoleCount} seeded roles.",
            user.Email,
            missingRoles.Length);
    }

    private bool CanBootstrapFirstUser()
    {
        return _hostEnvironment.IsDevelopment() || _hostEnvironment.IsEnvironment("Testing");
    }

    private static string ResolveDefaultDisplayName(ApplicationUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email.Trim();
        }

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            return user.UserName.Trim();
        }

        return string.Empty;
    }

    private static string FormatErrors(IdentityResult result)
    {
        return string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
    }
}
