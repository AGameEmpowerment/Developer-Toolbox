namespace SystemUptimeTracker.Qa.Automation.Support
{
    public interface IPlaywrightPageSessionFactory
    {
        Task<IPlaywrightPageSession> CreateAsync();
    }
}