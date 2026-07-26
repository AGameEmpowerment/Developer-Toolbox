namespace SystemUptimeTracker.Qa.Automation.Services;

public interface ITestIdentityAccountCleanupService
{
    Task<int> DeleteProvisionedAccountsAsync(CancellationToken cancellationToken = default);
}
