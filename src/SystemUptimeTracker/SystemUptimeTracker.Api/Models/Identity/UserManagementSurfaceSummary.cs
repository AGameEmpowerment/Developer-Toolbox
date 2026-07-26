namespace SystemUptimeTracker.Api.Models.Identity;

public sealed record UserManagementSurfaceSummary(
    string UserId,
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> Roles,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);
