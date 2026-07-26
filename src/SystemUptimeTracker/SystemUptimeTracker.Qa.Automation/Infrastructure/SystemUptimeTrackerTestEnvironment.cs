namespace SystemUptimeTracker.Qa.Automation.Infrastructure;

internal static class SystemUptimeTrackerTestEnvironment
{
    public static string Resolve()
    {
        return Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
               ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
               ?? "Development";
    }
}
