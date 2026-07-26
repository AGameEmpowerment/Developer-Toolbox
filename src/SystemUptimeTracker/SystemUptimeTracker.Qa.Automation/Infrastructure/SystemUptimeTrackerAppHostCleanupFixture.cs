using NUnit.Framework;

namespace SystemUptimeTracker.Qa.Automation.Infrastructure;

[SetUpFixture]
public sealed class SystemUptimeTrackerAppHostCleanupFixture
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        SystemUptimeTrackerAppHostManager.ForceCleanup();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        SystemUptimeTrackerAppHostManager.ForceCleanup();
    }
}
