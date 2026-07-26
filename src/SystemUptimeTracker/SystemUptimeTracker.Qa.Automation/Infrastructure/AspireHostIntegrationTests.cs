using Microsoft.Extensions.Logging;
using SystemUptimeTracker.Qa.Automation.TestBases;

namespace SystemUptimeTracker.Qa.Automation.Infrastructure;

[TestFixture(Category = "Automation"), Category("Integration"), Category("Aspire"), Category("Smoke")]
public sealed class AspireHostIntegrationTests : SystemUptimeTrackerFunctionalTestBase
{
    [Test]
    public void AppHost_StartsServerApiBackend()
    {
        Logger.LogInformation("Starting Aspire AppHost server API integration smoke test.");

        try
        {
            SystemUptimeTrackerAppHostManager.Acquire(SystemUptimeTrackerAppHostReadinessScope.SERVER_ONLY);
            Assert.Pass("Aspire AppHost started and the server API backend responded successfully.");
        }
        finally
        {
            SystemUptimeTrackerAppHostManager.Release();
        }
    }
}
