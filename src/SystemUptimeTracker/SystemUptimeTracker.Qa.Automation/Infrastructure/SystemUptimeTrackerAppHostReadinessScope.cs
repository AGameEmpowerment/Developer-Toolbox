namespace SystemUptimeTracker.Qa.Automation.Infrastructure
{
    internal enum SystemUptimeTrackerAppHostReadinessScope
    {
        DASHBOARD_ONLY = -1,
        SERVER_ONLY = 0,
        SERVER_AND_CLIENT = 1
    }
}