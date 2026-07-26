using SystemUptimeTracker.Api.Services.Identity;

namespace SystemUptimeTracker.Api.Extensions;

public static class IdentityInitializationExtensions
{
    public static async Task<WebApplication> InitializeIdentityAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = app.Services.CreateScope();
        IIdentityRoleSeeder identityRoleSeeder = scope.ServiceProvider.GetRequiredService<IIdentityRoleSeeder>();
        await identityRoleSeeder.EnsureSeedDataAsync(cancellationToken);
        return app;
    }
}