using Microsoft.Extensions.Options;
using SystemUptimeTracker.Qa.Automation.Support;

namespace SystemUptimeTracker.Qa.Automation.Configuration;

public sealed class SystemUptimeTrackerPageCatalogTests
{
    [Test]
    public void GetPageUrl_WhenUsingExternalHostAndPagePathIsMissing_UsesFallbackPathOnExternalBase()
    {
        SystemUptimeTrackerPageCatalog catalog = CreateCatalog(
            new SystemUptimeTrackerWebValidationOptions
            {
                BaseUrl = "https://localhost:3001",
            },
            new QaAutomationExecutionOptions
            {
                UseExternalHost = true,
                WebBaseUrl = "https://systemuptimetracker.example",
            });

        string pageUrl = catalog.GetPageUrl("Books", "https://localhost:3001/books");

        Assert.That(pageUrl, Is.EqualTo("https://systemuptimetracker.example/books"));
    }

    [Test]
    public void GetPageUrl_WhenUsingExternalHostAndHomePagePathIsMissing_UsesExternalBaseUrl()
    {
        SystemUptimeTrackerPageCatalog catalog = CreateCatalog(
            new SystemUptimeTrackerWebValidationOptions
            {
                BaseUrl = "https://localhost:3001",
            },
            new QaAutomationExecutionOptions
            {
                UseExternalHost = true,
                WebBaseUrl = "https://systemuptimetracker.example/",
            });

        string pageUrl = catalog.GetPageUrl("Home", "https://localhost:3001/");

        Assert.That(pageUrl, Is.EqualTo("https://systemuptimetracker.example"));
    }

    private static SystemUptimeTrackerPageCatalog CreateCatalog(
        SystemUptimeTrackerWebValidationOptions webValidationOptions,
        QaAutomationExecutionOptions executionOptions)
    {
        return new SystemUptimeTrackerPageCatalog(
            Options.Create(webValidationOptions),
            Options.Create(executionOptions));
    }
}