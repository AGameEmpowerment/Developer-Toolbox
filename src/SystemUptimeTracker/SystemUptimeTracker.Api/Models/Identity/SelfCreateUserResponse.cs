namespace SystemUptimeTracker.Api.Models.Identity;

public sealed record SelfCreateUserResponse(
    string UserId,
    string Email,
    string DisplayName,
    bool IsFirstUser,
    bool RequiresRoleAssignment,
    IReadOnlyCollection<string> Roles);
