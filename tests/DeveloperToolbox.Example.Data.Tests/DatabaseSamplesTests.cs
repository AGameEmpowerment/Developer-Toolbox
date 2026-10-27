using DeveloperToolbox.Example.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DeveloperToolbox.Example.Data.Tests;

public sealed class DatabaseSamplesTests
{
    [TestCase("", "payload", "queue")]
    [TestCase("id", "", "queue")]
    [TestCase("id", "payload", "")]
    public async Task SaveSqlServerMessageAsync_EmptyFields_RejectsBeforeConnecting(string id, string text, string source)
    {
        var samples = new DatabaseSamples(new ConfigurationBuilder().Build());
        await Assert.CatchAsync<ArgumentException>(() => samples.SaveSqlServerMessageAsync(id, text, source));
    }

    [TestCase(129, 1, 1)]
    [TestCase(1, 4097, 1)]
    [TestCase(1, 1, 65)]
    public async Task SavePostgresMessageAsync_OversizedFields_RejectsBeforeConnecting(int idLength, int textLength, int sourceLength)
    {
        var samples = new DatabaseSamples(new ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<ArgumentException>(() => samples.SavePostgresMessageAsync(
            new string('i', idLength), new string('t', textLength), new string('s', sourceLength)));
    }

    [Test]
    public async Task SqlServerAsync_RemoteDatabase_RejectsBeforeConnecting()
    {
        var samples = CreateSamples("SqlServer", "Server=remote.example,1433;Database=ToolboxSamples;User Id=user;Password=placeholder");
        await Assert.ThrowsAsync<InvalidOperationException>(() => samples.SqlServerAsync());
    }

    [Test]
    public async Task SqlServerAsync_FailoverPartner_RejectsBeforeConnecting()
    {
        var samples = CreateSamples("SqlServer", "Server=localhost;Failover Partner=remote.example;Database=ToolboxSamples;User Id=user;Password=placeholder");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<InvalidOperationException>(() => samples.SqlServerAsync(cancellationToken: cancellation.Token));
    }

    [Test]
    public async Task PostgresAsync_DifferentDatabase_RejectsBeforeConnecting()
    {
        var samples = CreateSamples("Postgres", "Host=localhost;Database=other-database;Username=user;Password=placeholder");
        await Assert.ThrowsAsync<InvalidOperationException>(() => samples.PostgresAsync());
    }

    [Test]
    public async Task PostgresAsync_MissingPassword_RejectsBeforeConnecting()
    {
        var samples = CreateSamples("Postgres", "Host=localhost;Database=ToolboxSamples;Username=user");
        await Assert.ThrowsAsync<InvalidOperationException>(() => samples.PostgresAsync());
    }

    [Test]
    public void Contexts_SameModel_EnforceStableMessageKeyAndPayloadBounds()
    {
        using var sql = new SqlServerSampleContext(new DbContextOptionsBuilder<SqlServerSampleContext>()
            .UseSqlServer("Server=localhost;Database=ToolboxSamples;Integrated Security=True").Options);
        using var postgres = new PostgresSampleContext(new DbContextOptionsBuilder<PostgresSampleContext>()
            .UseNpgsql("Host=localhost;Database=ToolboxSamples;Username=unused;Password=unused").Options);
        foreach (var context in new SampleContext[] { sql, postgres })
        {
            var entity = context.Model.FindEntityType(typeof(SampleMessage))!;
            var keyProperties = entity.FindPrimaryKey()!.Properties;
            Assert.That(keyProperties, Has.Count.EqualTo(1));
            Assert.That(keyProperties[0].Name, Is.EqualTo(nameof(SampleMessage.Id)));
            Assert.That(entity.FindProperty(nameof(SampleMessage.Id))!.GetMaxLength(), Is.EqualTo(128));
            Assert.That(entity.FindProperty(nameof(SampleMessage.Text))!.GetMaxLength(), Is.EqualTo(4096));
            Assert.That(entity.FindProperty(nameof(SampleMessage.Source))!.GetMaxLength(), Is.EqualTo(64));
            Assert.That(entity.FindProperty(nameof(SampleMessage.Text))!.IsNullable, Is.False);
        }
    }

    private static DatabaseSamples CreateSamples(string provider, string connection)
    {
        return new DatabaseSamples(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [$"ConnectionStrings:{provider}"] = connection }).Build());
    }
}
