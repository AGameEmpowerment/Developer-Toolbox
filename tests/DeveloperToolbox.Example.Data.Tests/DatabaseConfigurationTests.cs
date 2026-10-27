using DeveloperToolbox.Example.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperToolbox.Example.Data.Tests;

public sealed class DatabaseConfigurationTests
{
    [TestCase("sqlserver", null)]
    [TestCase("sqlserver", "")]
    [TestCase("sqlserver", " \t")]
    [TestCase("postgres", null)]
    [TestCase("postgres", "")]
    [TestCase("postgres", " \t")]
    public async Task TransactionSample_InvalidText_RejectsBeforeRequiringConfiguration(string provider, string? text)
    {
        var samples = new DatabaseSamples(new ConfigurationBuilder().Build());
        await Assert.CatchAsync<ArgumentException>(() => provider == "sqlserver"
            ? samples.SqlServerAsync(text!) : samples.PostgresAsync(text!));
    }

    [TestCase("sqlserver")]
    [TestCase("postgres")]
    public async Task TransactionSample_OversizedText_RejectsBeforeRequiringConfiguration(string provider)
    {
        var samples = new DatabaseSamples(new ConfigurationBuilder().Build());
        var text = new string('x', 4097);
        await Assert.ThrowsAsync<ArgumentException>(() => provider == "sqlserver"
            ? samples.SqlServerAsync(text) : samples.PostgresAsync(text));
    }

    [TestCase("sqlserver", false)]
    [TestCase("sqlserver", true)]
    [TestCase("postgres", false)]
    [TestCase("postgres", true)]
    public async Task DatabaseOperation_MissingConnection_ReportsOnlyRequiredSetting(string provider, bool save)
    {
        var samples = new DatabaseSamples(new ConfigurationBuilder().Build());
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(samples, provider, save));
        Assert.That(failure!.Message, Does.Contain(provider == "sqlserver" ? "ConnectionStrings__SqlServer" : "ConnectionStrings__Postgres"));
    }

    [TestCase("sqlserver", false)]
    [TestCase("sqlserver", true)]
    [TestCase("postgres", false)]
    [TestCase("postgres", true)]
    public async Task DatabaseOperation_PreCanceled_StopsBeforeConnecting(string provider, bool save)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SqlServer"] = "Server=127.0.0.1,10433;Database=ToolboxSamples;User ID=sa;Password=unit-test-placeholder",
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=ToolboxSamples;Username=postgres;Password=unit-test-placeholder"
        }).Build();
        var samples = new DatabaseSamples(configuration);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var failure = await Assert.CatchAsync<OperationCanceledException>(() => Execute(samples, provider, save, canceled.Token));
        Assert.That(failure!.CancellationToken, Is.EqualTo(canceled.Token));
    }

    [TestCase("sqlserver")]
    [TestCase("postgres")]
    public async Task SaveMessage_MaximumBounds_AcceptsValidationBeforeRequiringDatabase(string provider)
    {
        var samples = new DatabaseSamples(new ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider == "sqlserver"
            ? samples.SaveSqlServerMessageAsync(new string('i', 128), new string('t', 4096), new string('s', 64))
            : samples.SavePostgresMessageAsync(new string('i', 128), new string('t', 4096), new string('s', 64)));
    }

    [Test]
    public void AddToolboxDatabases_MissingProviderSettings_ResolvesOneLazySingleton()
    {
        var collection = new ServiceCollection();
        Assert.That(collection.AddToolboxDatabases(new ConfigurationBuilder().Build()), Is.SameAs(collection));
        using var provider = collection.BuildServiceProvider();

        var contract = provider.GetRequiredService<IDatabaseSamples>();
        Assert.That(provider.GetRequiredService<IDatabaseSamples>(), Is.SameAs(contract));
        Assert.That(provider.GetRequiredService<DatabaseSamples>(), Is.SameAs(contract));
    }

    [TestCase("SqlServer", "Database")]
    [TestCase("SqlServer", "Username")]
    [TestCase("SqlServer", "Password")]
    [TestCase("Postgres", "Database")]
    [TestCase("Postgres", "Username")]
    [TestCase("Postgres", "Password")]
    public async Task DatabaseOperation_MissingContainerComponent_ReportsSettingWithoutCredentials(string provider, string missing)
    {
        var values = ContainerSettings(provider);
        values.Remove($"DatabaseSamples:{provider}:{missing}");
        var samples = new DatabaseSamples(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(samples, provider.ToLowerInvariant(), true));

        Assert.That(failure!.Message, Does.Contain($"DatabaseSamples:{provider}:{missing}"));
        Assert.That(failure!.Message, Does.Not.Contain("placeholder-password"));
    }

    [TestCase("SqlServer")]
    [TestCase("Postgres")]
    public async Task DatabaseOperation_ContainerCredentialsWithDelimiters_PreservesLocalDatabase(string provider)
    {
        var values = ContainerSettings(provider);
        values[$"DatabaseSamples:{provider}:Username"] = "user;\"name\"";
        values[$"DatabaseSamples:{provider}:Password"] = "password;\"value\";Database=Forbidden;Host=remote.example";
        var samples = new DatabaseSamples(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var failure = await Assert.CatchAsync<OperationCanceledException>(() => Execute(samples, provider.ToLowerInvariant(), true, canceled.Token));
        Assert.That(failure!.CancellationToken, Is.EqualTo(canceled.Token));
    }

    [TestCase("SqlServer", "Server=localhost;Database=ToolboxSamples;User ID=sa;Password=placeholder-password")]
    [TestCase("Postgres", "Host=localhost;Database=ToolboxSamples;Username=postgres;Password=placeholder-password")]
    public async Task DatabaseOperation_ExplicitConnection_TakesPrecedenceOverContainerComponents(string provider, string connection)
    {
        var values = ContainerSettings(provider);
        values[$"DatabaseSamples:{provider}:Host"] = "remote.example";
        values[$"ConnectionStrings:{provider}"] = connection;
        var samples = new DatabaseSamples(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(() => Execute(samples, provider.ToLowerInvariant(), true, canceled.Token));
    }

    [TestCase("SqlServer", "Host", "remote.example")]
    [TestCase("SqlServer", "Database", "Forbidden")]
    [TestCase("Postgres", "Host", "remote.example")]
    [TestCase("Postgres", "Database", "Forbidden")]
    public async Task DatabaseOperation_UnsafeContainerDestination_RejectsBeforeConnecting(string provider, string key, string value)
    {
        var values = ContainerSettings(provider);
        values[$"DatabaseSamples:{provider}:{key}"] = value;
        var samples = new DatabaseSamples(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(samples, provider.ToLowerInvariant(), true));
        Assert.That(failure!.Message, Does.Contain("local toolbox host"));
    }

    private static Dictionary<string, string?> ContainerSettings(string provider) => new()
    {
        [$"DatabaseSamples:{provider}:Host"] = provider == "SqlServer" ? "mssql,1433" : "postgres",
        [$"DatabaseSamples:{provider}:Database"] = "ToolboxSamples",
        [$"DatabaseSamples:{provider}:Username"] = "sample-user",
        [$"DatabaseSamples:{provider}:Password"] = "placeholder-password"
    };

    private static async Task Execute(DatabaseSamples samples, string provider, bool save, CancellationToken token = default)
    {
        if (save)
        {
            if (provider == "sqlserver") { await samples.SaveSqlServerMessageAsync("sample-id", "sample", "unit-test", token); }
            else { await samples.SavePostgresMessageAsync("sample-id", "sample", "unit-test", token); }
        }
        else
        {
            if (provider == "sqlserver") { await samples.SqlServerAsync(cancellationToken: token); }
            else { await samples.PostgresAsync(cancellationToken: token); }
        }
    }
}
