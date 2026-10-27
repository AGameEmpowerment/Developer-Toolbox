using DeveloperToolbox.Example.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DeveloperToolbox.Example.Data.Tests;

public sealed class DatabaseIntegrationTests
{
    [SetUp]
    public void RequireDatabaseIntegrationOptIn()
    {
        if (Environment.GetEnvironmentVariable("TOOLBOX_RUN_DATABASE_INTEGRATION") != "1")
        {
            Assert.Ignore("Set TOOLBOX_RUN_DATABASE_INTEGRATION=1 and both database connections to run real-container tests.");
        }
    }

    [TestCase("SqlServer")]
    [TestCase("Postgres")]
    public async Task SaveMessageAsync_ConcurrentRedelivery_InsertsOneRow(string provider)
    {
        var connection = Environment.GetEnvironmentVariable($"ConnectionStrings__{provider}")
            ?? throw new InvalidOperationException($"Set ConnectionStrings__{provider} before running database integration tests.");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [$"ConnectionStrings:{provider}"] = connection }).Build();
        var samples = new DatabaseSamples(configuration);
        var id = $"integration-{Guid.NewGuid():N}";
        Func<Task<bool>> save = provider == "SqlServer"
            ? () => samples.SaveSqlServerMessageAsync(id, "Concurrent delivery", "integration-test")
            : () => samples.SavePostgresMessageAsync(id, "Concurrent delivery", "integration-test");

        await using SampleContext context = provider == "SqlServer"
            ? new SqlServerSampleContext(new DbContextOptionsBuilder<SqlServerSampleContext>().UseSqlServer(connection).Options)
            : new PostgresSampleContext(new DbContextOptionsBuilder<PostgresSampleContext>().UseNpgsql(connection).Options);
        try
        {
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => save()));
            Assert.That(outcomes.Count(inserted => inserted), Is.EqualTo(1));
            Assert.That(await save(), Is.False);
            Assert.That(await context.Messages.CountAsync(message => message.Id == id), Is.EqualTo(1));
        }
        finally
        {
            // Clean up only this test's correlated row; never drop shared sample databases.
            await context.Messages.Where(message => message.Id == id).ExecuteDeleteAsync();
        }
    }
}
