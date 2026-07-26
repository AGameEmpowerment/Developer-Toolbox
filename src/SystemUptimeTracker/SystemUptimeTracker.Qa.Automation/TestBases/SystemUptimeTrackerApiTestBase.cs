using SystemUptimeTracker.Qa.Automation.Support;
using Microsoft.Extensions.Logging;
using SystemUptimeTracker.Qa.Automation.Infrastructure;
using SystemUptimeTracker.Qa.Automation.Services;

namespace SystemUptimeTracker.Qa.Automation.TestBases;

public abstract class SystemUptimeTrackerApiTestBase : QaApiTestBase
{
    protected ISystemUptimeTrackerApiClient SystemUptimeTrackerApi { get; private set; } = null!;

    protected override bool IncludeKeyVault => false;

    protected override string EnvironmentName => SystemUptimeTrackerTestEnvironment.Resolve();

    protected override string[] CreateHostArgs()
    {
        return SystemUptimeTrackerAppHostManager.CreateQaAutomationHostArgs();
    }

    protected override void OnBeforeHostCreated()
    {
        if (QaAutomationExecution.UseExternalHost)
        {
            return;
        }

        SystemUptimeTrackerAppHostManager.Acquire(SystemUptimeTrackerAppHostReadinessScope.SERVER_ONLY);
    }

    protected override void OnHostCreationFailed()
    {
        if (QaAutomationExecution.UseExternalHost)
        {
            return;
        }

        SystemUptimeTrackerAppHostManager.Release();
    }

    protected override void OnHostReady()
    {
        base.OnHostReady();
        SystemUptimeTrackerApi = GetRequiredService<ISystemUptimeTrackerApiClient>();
        Logger.LogInformation(
            "QA automation API test host is ready for environment {EnvironmentName}.",
            EnvironmentName);
    }

    protected override void OnAfterHostDisposed()
    {
        if (!QaAutomationExecution.UseExternalHost)
        {
            SystemUptimeTrackerAppHostManager.Release();
        }

        base.OnAfterHostDisposed();
    }
}
