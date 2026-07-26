namespace SystemUptimeTracker.Api.Extensions;

internal static class StartupMigrationPolicy
{
    private const string APPLY_STARTUP_MIGRATIONS_ENVIRONMENT_VARIABLE = "SystemUptimeTracker__ApplyStartupMigrations";

    public static bool CanApply(IHostEnvironment hostEnvironment)
    {
        if (hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Testing"))
        {
            return true;
        }

        return bool.TryParse(
            Environment.GetEnvironmentVariable(APPLY_STARTUP_MIGRATIONS_ENVIRONMENT_VARIABLE),
            out bool applyStartupMigrations) && applyStartupMigrations;
    }
}