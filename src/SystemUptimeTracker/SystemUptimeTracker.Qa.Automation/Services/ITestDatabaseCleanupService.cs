namespace SystemUptimeTracker.Qa.Automation.Services;

public interface ITestDatabaseCleanupService
{
    /// <summary>
    /// Drops and recreates the QA database, applying all pending migrations.
    /// This ensures a clean, deterministic database state for test execution.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ResetDatabaseAsync(CancellationToken cancellationToken = default);
}
