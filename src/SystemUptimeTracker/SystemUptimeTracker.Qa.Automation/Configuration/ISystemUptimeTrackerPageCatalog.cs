namespace SystemUptimeTracker.Qa.Automation.Configuration;

public interface ISystemUptimeTrackerPageCatalog
{
    bool UseInternalPages { get; }

    string GetPageUrl(string pageKey, string fallbackUrl);

    string GetPageTitle(string pageKey, string fallbackTitle);
}
