using SystemUptimeTracker.Qa.Automation.Support;
using SystemUptimeTracker.Qa.Automation.Infrastructure;

namespace SystemUptimeTracker.Qa.Automation.TestBases;

public abstract class SystemUptimeTrackerFunctionalTestBase : QaTestBase
{
    protected override bool IncludeKeyVault => false;

    protected override string EnvironmentName => SystemUptimeTrackerTestEnvironment.Resolve();

}
