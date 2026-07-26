namespace SystemUptimeTracker.Qa.Automation.Services
{
    public sealed record TestIdentityAccountProvisioningResult(
        string Email,
        string Password,
        bool UserCreated,
        bool PasswordReset,
        bool EmailConfirmed,
        bool SignInValidated,
        bool CleanupScheduled,
        IReadOnlyList<string> RequiredRoles,
        IReadOnlyList<string> AssignedRoles);
}
