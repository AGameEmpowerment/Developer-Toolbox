using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SystemUptimeTracker.Data.Identity;

namespace SystemUptimeTracker.Qa.Automation.Services;

public sealed class TestDatabaseCleanupService : ITestDatabaseCleanupService
{
    private readonly ApplicationDbContext _identityDbContext;
    private readonly ILogger<TestDatabaseCleanupService> _logger;

    public TestDatabaseCleanupService(
        ApplicationDbContext identityDbContext,
        ILogger<TestDatabaseCleanupService> logger)
    {
        _identityDbContext = identityDbContext;
        _logger = logger;
    }

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting QA database cleanup and reset.");

            await DropAndRecreateAsync(_identityDbContext, cancellationToken);

            _logger.LogInformation("QA database cleanup and reset completed successfully.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to reset QA database.");
            throw;
        }
    }

    private async Task DropAndRecreateAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        string dbName = dbContext.Database.GetDbConnection().Database;
        _logger.LogInformation("Resetting database: {DatabaseName}", dbName);

        try
        {
            await dbContext.Database.EnsureDeletedAsync(cancellationToken);

            // Clear connection pool to ensure no stale connections
            SqlConnection.ClearAllPools();

            // Give SQL Server a moment to fully release the database
            await Task.Delay(100, cancellationToken);

            _logger.LogDebug("Dropped database: {DatabaseName}", dbName);
        }
        catch (SqlException exception) when (exception.Number == 3701 || exception.Number == 15457)
        {
            _logger.LogDebug(exception, "Database {DatabaseName} did not exist; proceeding with creation.", dbName);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Error dropping database {DatabaseName}; attempting to proceed with creation.", dbName);
        }

        await dbContext.Database.MigrateAsync(cancellationToken);
        _logger.LogDebug("Applied all pending migrations to database: {DatabaseName}", dbName);
    }
}
