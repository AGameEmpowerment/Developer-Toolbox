using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SystemUptimeTracker.Qa.Automation.Configuration;
using SystemUptimeTracker.Qa.Automation.Support;

namespace SystemUptimeTracker.Qa.Automation;

[TestFixture]
public sealed class RegisterDependentServicesTests
{
    [Test]
    public void RegisterQaAutomationServices_WhenUsingDefaultConfiguration_BindsStableLocalBaseUrls()
    {
        ServiceProvider services = new ServiceCollection()
            .RegisterQaAutomationServices(environmentName: "Development")
            .BuildServiceProvider(validateScopes: true);

        try
        {
            AutomationAppSettings appSettings = services.GetRequiredService<IOptions<AutomationAppSettings>>().Value;
            SystemUptimeTrackerWebValidationOptions webValidation =
                services.GetRequiredService<IOptions<SystemUptimeTrackerWebValidationOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(appSettings.BaseUrl, Is.EqualTo("https://localhost:7060/"));
                Assert.That(webValidation.BaseUrl, Is.EqualTo("https://localhost:3001"));
            });
        }
        finally
        {
            services.Dispose();
        }
    }
}
