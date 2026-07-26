namespace SystemUptimeTracker.Api.Services.Identity;

public interface IIdentityRoleSeeder
{
    Task EnsureSeedDataAsync(CancellationToken cancellationToken = default);
}