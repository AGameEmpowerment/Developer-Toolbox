namespace DeveloperToolbox.Example.Data;

/// <summary>The database operations used by the CLI and serverless examples.</summary>
public interface IDatabaseSamples
{
    /// <summary>Commits a sample SQL Server message and reads recent messages.</summary>
    Task<DatabaseSampleResult> SqlServerAsync(string text = "Hello SQL Server", CancellationToken cancellationToken = default);

    /// <summary>Commits a sample PostgreSQL message and reads recent messages.</summary>
    Task<DatabaseSampleResult> PostgresAsync(string text = "Hello PostgreSQL", CancellationToken cancellationToken = default);

    /// <summary>Persists SQL Server queue work once using a stable delivery ID.</summary>
    Task<bool> SaveSqlServerMessageAsync(string messageId, string text, string source, CancellationToken cancellationToken = default);

    /// <summary>Persists PostgreSQL queue work once using a stable delivery ID.</summary>
    Task<bool> SavePostgresMessageAsync(string messageId, string text, string source, CancellationToken cancellationToken = default);
}
