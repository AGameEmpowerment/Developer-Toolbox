namespace SystemUptimeTracker.Qa.Automation.Support
{
    public sealed class ApiClientConfiguration
    {
        public bool AllowLoopbackCertificateBypass { get; init; }

        public int TcpTimeoutInSeconds { get; init; } = 120;
    }
}