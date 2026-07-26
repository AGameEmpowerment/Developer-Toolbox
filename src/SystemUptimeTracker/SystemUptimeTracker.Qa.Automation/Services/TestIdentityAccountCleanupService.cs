using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemUptimeTracker.Data.Identity;
using SystemUptimeTracker.Qa.Automation.Support;

namespace SystemUptimeTracker.Qa.Automation.Services;

public sealed class TestIdentityAccountCleanupService : ITestIdentityAccountCleanupService
{
    private readonly AutomationAppSettings _appSettings;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<TestIdentityAccountCleanupService> _logger;
    private readonly UserManager<ApplicationUser> _userManager;

    public TestIdentityAccountCleanupService(
        IOptions<AutomationAppSettings> appSettings,
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ILogger<TestIdentityAccountCleanupService> logger)
    {
        _appSettings = appSettings.Value;
        _dbContext = dbContext;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<int> DeleteProvisionedAccountsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseReadyAsync(cancellationToken);

        string emailPrefix = ResolveEmailPrefix(_appSettings.LocalIdentityTestUser);
        string emailDomain = ResolveEmailDomain(_appSettings.LocalIdentityTestUser);

        List<ApplicationUser> users = await _dbContext.Users
            .Where(user => user.Email != null
                           && user.Email.StartsWith(emailPrefix)
                           && user.Email.EndsWith(emailDomain))
            .ToListAsync(cancellationToken);

        foreach (ApplicationUser user in users)
        {
            IdentityResult deleteResult = await _userManager.DeleteAsync(user);
            EnsureSucceeded(deleteResult, $"delete QA identity account '{user.Email}'");
        }

        if (users.Count > 0)
        {
            _logger.LogInformation("Deleted {UserCount} QA identity account artifact(s) from AspNetUsers.", users.Count);
        }

        return users.Count;
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

    private static string ResolveEmailPrefix(LocalIdentityTestUserConfiguration settings)
    {
        string localPartPrefix = string.IsNullOrWhiteSpace(settings.EmailLocalPartPrefix)
            ? "systemuptimetracker-qa"
            : settings.EmailLocalPartPrefix.Trim();

        return $"{localPartPrefix}+";
    }

    private static string ResolveEmailDomain(LocalIdentityTestUserConfiguration settings)
    {
        string emailDomain = string.IsNullOrWhiteSpace(settings.EmailDomain)
            ? "example.invalid"
            : settings.EmailDomain.Trim().TrimStart('@');

        return $"@{emailDomain}";
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
