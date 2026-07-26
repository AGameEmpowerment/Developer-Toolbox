namespace SystemUptimeTracker.Qa.Automation.Support
{
    public interface IApiClientFactory
    {
        HttpClient InitHttpClient(string acceptHeader);
    }
}