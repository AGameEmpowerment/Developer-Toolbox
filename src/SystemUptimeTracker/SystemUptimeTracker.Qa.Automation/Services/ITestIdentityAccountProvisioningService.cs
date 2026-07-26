namespace SystemUptimeTracker.Qa.Automation.Services;

public interface ITestIdentityAccountProvisioningService : IAsyncDisposable
{
    Task<TestIdentityAccountProvisioningResult> EnsureIndividualAccountReadyAsync(CancellationToken cancellationToken = default);

    Task<TestIdentityAccountProvisioningResult> EnsureIndividualAccountReadyWithRolesAsync(
        IReadOnlyCollection<string> requiredRoles,
        string? displayName = null,
        CancellationToken cancellationToken = default);
}
