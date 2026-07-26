namespace SystemUptimeTracker.Api.Models.Identity;

public sealed record BootstrapAdminUserResponse(
    string UserId,
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> Roles);
