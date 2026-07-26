namespace SystemUptimeTracker.Qa.Automation.Infrastructure;

[TestFixture]
public sealed class SystemUptimeTrackerAppHostManagerTests
{
    [Test]
    public void IsDistributedApplicationStartedLine_WhenStartedMessageIsPresent_ReturnsTrue()
    {
        bool isStarted = SystemUptimeTrackerAppHostManager.IsDistributedApplicationStartedLine(
            "info: Aspire.Hosting.DistributedApplication[0] Distributed application started. Press Ctrl+C to shut down.");

        Assert.That(isStarted, Is.True);
    }

    [Test]
    public void RequiresClientReadiness_WhenScopeIsServerOnly_ReturnsFalse()
    {
        bool requiresClientReadiness = SystemUptimeTrackerAppHostManager.RequiresClientReadiness(SystemUptimeTrackerAppHostReadinessScope.SERVER_ONLY);

        Assert.That(requiresClientReadiness, Is.False);
    }

    [Test]
    public void RequiresClientReadiness_WhenScopeIncludesClient_ReturnsTrue()
    {
        bool requiresClientReadiness = SystemUptimeTrackerAppHostManager.RequiresClientReadiness(SystemUptimeTrackerAppHostReadinessScope.SERVER_AND_CLIENT);

        Assert.That(requiresClientReadiness, Is.True);
    }

    [Test]
    public void ReadinessScopeSatisfies_WhenActualScopeIncludesClientAndRequiredScopeIsServerOnly_ReturnsTrue()
    {
        bool satisfies = SystemUptimeTrackerAppHostManager.ReadinessScopeSatisfies(
            SystemUptimeTrackerAppHostReadinessScope.SERVER_AND_CLIENT,
            SystemUptimeTrackerAppHostReadinessScope.SERVER_ONLY);

        Assert.That(satisfies, Is.True);
    }

    [Test]
    public void ReadinessScopeSatisfies_WhenActualScopeIsServerOnlyAndRequiredScopeIncludesClient_ReturnsFalse()
    {
        bool satisfies = SystemUptimeTrackerAppHostManager.ReadinessScopeSatisfies(
            SystemUptimeTrackerAppHostReadinessScope.SERVER_ONLY,
            SystemUptimeTrackerAppHostReadinessScope.SERVER_AND_CLIENT);

        Assert.That(satisfies, Is.False);
    }

    [Test]
    public void ShouldAbortForStalledStartup_WhenApplicationHasNotStartedAndProgressExceededTimeout_ReturnsTrue()
    {
        DateTime utcNow = new(2026, 3, 30, 0, 2, 0, DateTimeKind.Utc);
        DateTime lastProgressUtc = utcNow.AddSeconds(-91);

        bool shouldAbort = SystemUptimeTrackerAppHostManager.ShouldAbortForStalledStartup(false, dashboardReady: false, lastProgressUtc, utcNow);

        Assert.That(shouldAbort, Is.True);
    }

    [Test]
    public void RequiresClientReadiness_WhenScopeIsDashboardOnly_ReturnsFalse()
    {
        bool requiresClientReadiness = SystemUptimeTrackerAppHostManager.RequiresClientReadiness(SystemUptimeTrackerAppHostReadinessScope.DASHBOARD_ONLY);

        Assert.That(requiresClientReadiness, Is.False);
    }

    [Test]
    public void ShouldAbortForStalledStartup_WhenApplicationAlreadyStarted_ReturnsFalse()
    {
        DateTime utcNow = new(2026, 3, 30, 0, 0, 45, DateTimeKind.Utc);
        DateTime lastProgressUtc = utcNow.AddMinutes(-5);

        bool shouldAbort = SystemUptimeTrackerAppHostManager.ShouldAbortForStalledStartup(true, dashboardReady: false, lastProgressUtc, utcNow);

        Assert.That(shouldAbort, Is.False);
    }

    [Test]
    public void ShouldAbortForStalledStartup_WhenDashboardIsReadyButAppNotStarted_ReturnsFalse()
    {
        // Once the dashboard is up, AppHost is alive; silence is child services compiling — not a stall.
        DateTime utcNow = new(2026, 3, 30, 0, 2, 0, DateTimeKind.Utc);
        DateTime lastProgressUtc = utcNow.AddSeconds(-91);

        bool shouldAbort = SystemUptimeTrackerAppHostManager.ShouldAbortForStalledStartup(false, dashboardReady: true, lastProgressUtc, utcNow);

        Assert.That(shouldAbort, Is.False);
    }

    [Test]
    public void PathsReferToSameFile_WhenPathsDifferOnlyByCase_ReturnsTrue()
    {
        string leftPath = Path.Combine("C:\\", "Code", "SystemUptimeTracker", "System-Uptime-Tracker", "src", "SystemUptimeTracker", "SystemUptimeTracker.Api", "bin", "Debug", "net10.0", "SystemUptimeTracker.Api.exe");
        string rightPath = Path.Combine("c:\\", "code", "systemuptimetracker", "System-Uptime-Tracker", "src", "SystemUptimeTracker", "SystemUptimeTracker.Api", "bin", "Debug", "net10.0", "SystemUptimeTracker.Api.exe");

        bool matches = SystemUptimeTrackerAppHostManager.PathsReferToSameFile(leftPath, rightPath);

        Assert.That(matches, Is.EqualTo(OperatingSystem.IsWindows()));
    }

    [Test]
    public void PathsReferToSameFile_WhenEitherPathIsMissing_ReturnsFalse()
    {
        bool matches = SystemUptimeTrackerAppHostManager.PathsReferToSameFile(null, "C:\\temp\\SystemUptimeTracker.Api.exe");

        Assert.That(matches, Is.False);
    }

    [Test]
    public void ShouldCleanupStaleAppHostTempDirectory_WhenOwnerProcessIsAlive_ReturnsFalse()
    {
        DateTime utcNow = new(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc);
        DateTime staleLastWriteUtc = utcNow.AddMinutes(-20);

        bool shouldCleanup = SystemUptimeTrackerAppHostManager.ShouldCleanupStaleAppHostTempDirectory(
            staleLastWriteUtc,
            utcNow,
            ownerProcessId: 42,
            static _ => true);

        Assert.That(shouldCleanup, Is.False);
    }

    [Test]
    public void ShouldCleanupStaleAppHostTempDirectory_WhenDirectoryIsOlderThanThresholdAndOwnerIsGone_ReturnsTrue()
    {
        DateTime utcNow = new(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc);
        DateTime staleLastWriteUtc = utcNow.AddMinutes(-20);

        bool shouldCleanup = SystemUptimeTrackerAppHostManager.ShouldCleanupStaleAppHostTempDirectory(
            staleLastWriteUtc,
            utcNow,
            ownerProcessId: 42,
            static _ => false);

        Assert.That(shouldCleanup, Is.True);
    }

    [Test]
    public void ShouldCleanupStaleAppHostTempDirectory_WhenDirectoryIsRecentAndOwnerIsGone_ReturnsFalse()
    {
        DateTime utcNow = new(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc);
        DateTime recentLastWriteUtc = utcNow.AddMinutes(-5);

        bool shouldCleanup = SystemUptimeTrackerAppHostManager.ShouldCleanupStaleAppHostTempDirectory(
            recentLastWriteUtc,
            utcNow,
            ownerProcessId: null,
            static _ => false);

        Assert.That(shouldCleanup, Is.False);
    }

    [Test]
    public void StartupLogContainsFatalError_WhenApiErrorStreamHasContent_ReturnsTrue()
    {
        bool containsFatalError = SystemUptimeTrackerAppHostManager.StartupLogContainsFatalError(
            "systemuptimetracker-api-abc_err_123.log",
            "System.InvalidOperationException: boom");

        Assert.That(containsFatalError, Is.True);
    }

    [Test]
    public void StartupLogContainsFatalError_WhenFrontendOutputContainsNpmError_ReturnsTrue()
    {
        bool containsFatalError = SystemUptimeTrackerAppHostManager.StartupLogContainsFatalError(
            "systemuptimetracker-web-installer-abc_out_123.log",
            "npm ERR! code ELIFECYCLE");

        Assert.That(containsFatalError, Is.True);
    }

    [Test]
    public void StartupLogContainsFatalError_WhenLogIsBenign_ReturnsFalse()
    {
        bool containsFatalError = SystemUptimeTrackerAppHostManager.StartupLogContainsFatalError(
            "systemuptimetracker-web-installer-abc_out_123.log",
            "up to date, audited 1249 packages in 3s");

        Assert.That(containsFatalError, Is.False);
    }

    [Test]
    public void BuildNextAllowedDevOrigins_WhenClientPortChanges_UsesThatPortForLoopbackOrigins()
    {
        string origins = SystemUptimeTrackerAppHostManager.BuildNextAllowedDevOrigins(31234);

        Assert.That(
            origins,
            Is.EqualTo(
                "host.docker.internal,host.docker.internal:31234,localhost,localhost:31234,127.0.0.1,127.0.0.1:31234"));
    }
}
