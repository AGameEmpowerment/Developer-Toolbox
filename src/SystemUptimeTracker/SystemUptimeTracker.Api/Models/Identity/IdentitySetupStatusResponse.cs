namespace SystemUptimeTracker.Api.Models.Identity;

public sealed record IdentitySetupStatusResponse(
    bool HasUsers,
    bool HasAdministrators,
    bool IsFirstTimeSetup,
    bool CanCreateFirstUser);
