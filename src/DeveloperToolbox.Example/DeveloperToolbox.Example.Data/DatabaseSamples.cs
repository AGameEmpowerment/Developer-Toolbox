using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace DeveloperToolbox.Example.Data;

/// <summary>Local-only EF Core examples; EnsureCreated is for disposable sample databases, not production migrations.</summary>
public sealed class DatabaseSamples(IConfiguration configuration) : IDatabaseSamples
{
    private readonly SemaphoreSlim sqlInitialization = new(1, 1);
    private readonly SemaphoreSlim postgresInitialization = new(1, 1);
    private bool sqlInitialized;
    private bool postgresInitialized;

    /// <summary>Creates the SQL Server schema, commits a message, then reads recent messages.</summary>
    public async Task<DatabaseSampleResult> SqlServerAsync(string text = "Hello SQL Server", CancellationToken cancellationToken = default)
    {
        var message = CreateMessage(Guid.NewGuid().ToString("N"), text, "database-sample");
        await using var context = CreateSqlServerContext();
        await InitializeAsync(context, true, cancellationToken);
        return await DemonstrateTransactionAsync(context, "SQL Server", message, cancellationToken);
    }

    /// <summary>Creates the PostgreSQL schema, commits a message, then reads recent messages.</summary>
    public async Task<DatabaseSampleResult> PostgresAsync(string text = "Hello PostgreSQL", CancellationToken cancellationToken = default)
    {
        var message = CreateMessage(Guid.NewGuid().ToString("N"), text, "database-sample");
        await using var context = CreatePostgresContext();
        await InitializeAsync(context, false, cancellationToken);
        return await DemonstrateTransactionAsync(context, "PostgreSQL", message, cancellationToken);
    }

    /// <summary>Persists a message once in SQL Server; true means this delivery inserted a row.</summary>
    public async Task<bool> SaveSqlServerMessageAsync(string messageId, string text, string source, CancellationToken cancellationToken = default)
    {
        var message = CreateMessage(messageId, text, source);
        await using var context = CreateSqlServerContext();
        await InitializeAsync(context, true, cancellationToken);
        return await SaveOnceAsync(context, message, cancellationToken);
    }

    /// <summary>Persists a message once in PostgreSQL; true means this delivery inserted a row.</summary>
    public async Task<bool> SavePostgresMessageAsync(string messageId, string text, string source, CancellationToken cancellationToken = default)
    {
        var message = CreateMessage(messageId, text, source);
        await using var context = CreatePostgresContext();
        await InitializeAsync(context, false, cancellationToken);
        return await SaveOnceAsync(context, message, cancellationToken);
    }

    // Open a context per operation; EF and the underlying provider pool database connections.
    private SqlServerSampleContext CreateSqlServerContext()
    {
        var connection = new SqlConnectionStringBuilder(RequiredConnection("SqlServer"));
        var host = connection.DataSource.Split(',')[0];
        ValidateLocalDatabase(host, connection.InitialCatalog);
        if (!string.IsNullOrEmpty(connection.FailoverPartner))
        {
            throw new InvalidOperationException("These examples require one local toolbox SQL Server without a failover partner.");
        }
        if (!connection.IntegratedSecurity && string.IsNullOrWhiteSpace(connection.Password))
        {
            throw new InvalidOperationException("Supply SQL Server credentials in ConnectionStrings:SqlServer.");
        }
        return new SqlServerSampleContext(new DbContextOptionsBuilder<SqlServerSampleContext>()
            .UseSqlServer(connection.ConnectionString).Options);
    }

    private PostgresSampleContext CreatePostgresContext()
    {
        var connection = new NpgsqlConnectionStringBuilder(RequiredConnection("Postgres"));
        ValidateLocalDatabase(connection.Host ?? "", connection.Database ?? "");
        if (string.IsNullOrWhiteSpace(connection.Password))
        {
            throw new InvalidOperationException("Supply PostgreSQL credentials in ConnectionStrings:Postgres.");
        }
        return new PostgresSampleContext(new DbContextOptionsBuilder<PostgresSampleContext>()
            .UseNpgsql(connection.ConnectionString).Options);
    }

    private string RequiredConnection(string name)
    {
        var connection = configuration.GetConnectionString(name);
        if (connection is not null)
        {
            return connection;
        }
        var prefix = $"DatabaseSamples:{name}";
        var host = configuration[$"{prefix}:Host"];
        if (host is null)
        {
            throw new InvalidOperationException($"Set ConnectionStrings__{name} to the local ToolboxSamples database.");
        }
        string Required(string key) => configuration[$"{prefix}:{key}"]
            ?? throw new InvalidOperationException($"Set {prefix}:{key} for the local database sample.");

        // Container secrets arrive as separate values; provider builders safely quote
        // semicolons and quotes instead of interpolating them into a connection string.
        if (name == "SqlServer")
        {
            var sql = new SqlConnectionStringBuilder
            {
                DataSource = host, InitialCatalog = Required("Database"),
                UserID = Required("Username"), Password = Required("Password"),
                TrustServerCertificate = true
            };
            sql["Encrypt"] = true;
            return sql.ConnectionString;
        }
        return new NpgsqlConnectionStringBuilder
        {
            Host = host, Database = Required("Database"),
            Username = Required("Username"), Password = Required("Password")
        }.ConnectionString;
    }

    private static void ValidateLocalDatabase(string host, string database)
    {
        if (host is not ("localhost" or "127.0.0.1" or "mssql" or "postgres") || database != "ToolboxSamples")
        {
            throw new InvalidOperationException("These examples require a local toolbox host and Database=ToolboxSamples.");
        }
    }

    private async Task InitializeAsync(SampleContext context, bool sqlServer, CancellationToken cancellationToken)
    {
        var gate = sqlServer ? sqlInitialization : postgresInitialization;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (sqlServer ? sqlInitialized : postgresInitialized)
            {
                return;
            }
            await context.Database.EnsureCreatedAsync(cancellationToken);
            if (sqlServer)
            {
                sqlInitialized = true;
            }
            else
            {
                postgresInitialized = true;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<DatabaseSampleResult> DemonstrateTransactionAsync(
        SampleContext context, string provider, SampleMessage message, CancellationToken cancellationToken)
    {
        // Two operations share one explicit transaction. Disposal rolls back if either fails.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.Messages.Add(message);
        await context.SaveChangesAsync(cancellationToken);
        var recent = await context.Messages.AsNoTracking().OrderByDescending(item => item.CreatedAtUtc)
            .Take(10).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DatabaseSampleResult(provider, message, recent);
    }

    private static async Task<bool> SaveOnceAsync(SampleContext context, SampleMessage message, CancellationToken cancellationToken)
    {
        if (await context.Messages.AnyAsync(item => item.Id == message.Id, cancellationToken))
        {
            return false;
        }
        context.Messages.Add(message);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            // Concurrent deliveries can pass the existence check. The database primary key is authoritative.
            context.ChangeTracker.Clear();
            if (await context.Messages.AnyAsync(item => item.Id == message.Id, cancellationToken))
            {
                return false;
            }
            throw;
        }
    }

    private static bool IsDuplicate(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 }
            or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }

    private static SampleMessage CreateMessage(string id, string text, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (id.Length > 128 || text.Length > 4096 || source.Length > 64)
        {
            throw new ArgumentException("Sample messages permit 128 ID, 4096 text, and 64 source characters.");
        }
        return new SampleMessage { Id = id, Text = text, Source = source };
    }
}
