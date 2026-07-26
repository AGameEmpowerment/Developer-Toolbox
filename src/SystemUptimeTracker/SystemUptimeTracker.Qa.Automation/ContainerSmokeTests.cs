using SystemUptimeTracker.Qa.Automation.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SystemUptimeTracker.Qa.Automation.Configuration;
using SystemUptimeTracker.Qa.Automation.Infrastructure;
using SystemUptimeTracker.Qa.Automation.Pages;
using SystemUptimeTracker.Qa.Automation.Services;
using SystemUptimeTracker.Qa.Automation.TestBases;

namespace SystemUptimeTracker.Qa.Automation;

[TestFixture(Category = "Automation"), Category("Integration"), Category("Smoke"), Category("Container")]
public sealed class ContainerSmokeTests : SystemUptimeTrackerFunctionalTestBase
{
    [Test]
    public void Host_ResolvesSystemUptimeTrackerAutomationDependencies()
    {
        Logger.LogInformation("Resolving QA automation container dependencies.");
        using IServiceScope scope = Services.CreateScope();
        IServiceProvider scopedServices = scope.ServiceProvider;

        Assert.Multiple(() =>
        {
            Assert.That(GetRequiredService<ISystemUptimeTrackerApiClient>(), Is.Not.Null);
            Assert.That(GetRequiredService<ISystemUptimeTrackerPageCatalog>(), Is.Not.Null);
            Assert.That(GetRequiredService<ILogger<ContainerSmokeTests>>(), Is.Not.Null);
            Assert.That(GetRequiredService<IApiClientFactory>(), Is.Not.Null);
            Assert.That(GetRequiredService<IPlaywrightBrowserEnvironment>(), Is.Not.Null);
            Assert.That(scopedServices.GetRequiredService<IPlaywrightBrowserFactory>(), Is.Not.Null);
            Assert.That(scopedServices.GetRequiredService<IPlaywrightPageSessionFactory>(), Is.Not.Null);
            Assert.That(scopedServices.GetRequiredService<IPageObjectFactory>(), Is.Not.Null);
        });

        Logger.LogInformation("Container resolved all non-browser QA automation services successfully.");
    }
}
