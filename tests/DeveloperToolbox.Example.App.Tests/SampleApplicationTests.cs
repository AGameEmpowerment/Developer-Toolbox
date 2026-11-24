using System.Text.Json;
using DeveloperToolbox.Example.App;
using DeveloperToolbox.Example.Data;
using DeveloperToolbox.Example.Lib;
using DeveloperToolbox.Example.Messaging;
using DeveloperToolbox.Example.Resources;
using NUnit.Framework;

namespace DeveloperToolbox.Example.App.Tests;

public sealed class SampleApplicationTests
{
    [Test]
    public async Task RunAsync_NoArguments_PrintsGreetingWithoutStartingClients()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SampleApplication.RunAsync([], UnexpectedRunner, output, error);

        Assert.That(code, Is.EqualTo(0));
        Assert.That(output.ToString(), Does.StartWith(new Dependency().GetMessage() + Environment.NewLine));
        Assert.That(output.ToString(), Does.Contain("--help"));
        Assert.That(error.ToString(), Is.Empty);
    }

    [TestCase("--help")]
    [TestCase("-h")]
    [TestCase("--HELP")]
    public async Task RunAsync_Help_PrintsAllResourcesWithoutStartingClients(string argument)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SampleApplication.RunAsync([argument], UnexpectedRunner, output, error);

        Assert.That(code, Is.EqualTo(0));
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines.Take(SampleApplication.ResourceNames.Count), Is.EqualTo(SampleApplication.ResourceNames));
        Assert.That(lines, Does.Contain("validate [resource ...] [--json] [--timeout-seconds 45]"));
        Assert.That(error.ToString(), Is.Empty);
    }

    [TestCase("unsupported")]
    [TestCase("")]
    public async Task RunAsync_UnknownResource_ReturnsUsageErrorWithoutStartingClients(string argument)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SampleApplication.RunAsync([argument], UnexpectedRunner, output, error);

        Assert.That(code, Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.Contain("Unknown resource"));
    }

    public static IEnumerable<object[]> ResourceArguments => SampleApplication.ResourceNames.Select(name => new object[] { name });

    [TestCaseSource(nameof(ResourceArguments))]
    public async Task RunAsync_SupportedResource_CallsOnlySelectedExampleWithCallerToken(string resource)
    {
        var providers = new FakeProviders();
        using var tokenSource = new CancellationTokenSource();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SampleApplication.RunAsync([resource.ToUpperInvariant()],
            () => new SampleRunner(providers, providers, providers), output, error, tokenSource.Token);

        Assert.That(code, Is.EqualTo(0));
        Assert.That(providers.Calls, Has.Count.EqualTo(1));
        Assert.That(providers.Calls[0], Is.EqualTo((resource, tokenSource.Token)));
        Assert.That(error.ToString(), Is.Empty);
        if (resource is "sqlserver" or "postgres")
        {
            using var result = JsonDocument.Parse(output.ToString());
            Assert.That(result.RootElement.GetProperty("Provider").GetString(), Is.EqualTo(resource));
            Assert.That(result.RootElement.GetProperty("Inserted").GetProperty("Text").GetString(), Is.EqualTo(resource == "sqlserver" ? "Hello SQL Server" : "Hello PostgreSQL"));
        }
        else
        {
            Assert.That(output.ToString(), Is.EqualTo(resource + " result" + Environment.NewLine));
        }
    }

    [Test]
    public async Task RunAsync_ProviderFails_ReturnsFailureWithoutPrintingSecrets()
    {
        var providers = new FakeProviders { Failure = new InvalidOperationException("password=synthetic-sensitive-value") };
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SampleApplication.RunAsync(["s3"], () => new SampleRunner(providers, providers, providers), output, error);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.Contain("s3 sample failed (InvalidOperationException)"));
        Assert.That(error.ToString(), Does.Not.Contain("synthetic-sensitive-value"));
    }

    [Test]
    public async Task RunAsync_HostConfigurationFails_ReturnsSafeFailure()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SampleApplication.RunAsync(["postgres"],
            () => throw new FormatException("synthetic-secret-setting"), output, error);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(error.ToString(), Does.Contain("FormatException"));
        Assert.That(error.ToString(), Does.Not.Contain("synthetic-secret-setting"));
    }

    [Test]
    public async Task RunAsync_ProviderCanceled_ReturnsFailureAndForwardsCancellation()
    {
        using var tokenSource = new CancellationTokenSource();
        tokenSource.Cancel();
        var providers = new FakeProviders { Failure = new OperationCanceledException(tokenSource.Token) };
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SampleApplication.RunAsync(["redis"], () => new SampleRunner(providers, providers, providers),
            output, error, tokenSource.Token);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(providers.Calls, Has.Count.EqualTo(1));
        Assert.That(providers.Calls[0].Token, Is.EqualTo(tokenSource.Token));
        Assert.That(error.ToString(), Does.Contain("OperationCanceledException"));
    }

    private static SampleRunner UnexpectedRunner() => throw new InvalidOperationException("The host must not start for this command.");

    [Test]
    public async Task RunAsync_ValidateSelectedResources_ReusesRunnerAndCallsOnlyRequestedChecks()
    {
        var providers = new FakeProviders();
        var initializations = 0;
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SampleApplication.RunAsync(["validate", "REDIS", "wiremock", "redis", "--json"],
            () => { initializations++; return new SampleRunner(providers, providers, providers); }, output, error);

        Assert.That(code, Is.EqualTo(0));
        Assert.That(initializations, Is.EqualTo(1));
        Assert.That(providers.Calls.Select(call => call.Resource), Is.EqualTo(new[] { "redis", "wiremock" }));
        Assert.That(providers.Calls.Select(call => call.Token.CanBeCanceled), Is.All.True);
        Assert.That(error.ToString(), Is.Empty);
        using var result = JsonDocument.Parse(output.ToString());
        Assert.That(result.RootElement.GetProperty("passed").GetInt32(), Is.EqualTo(2));
    }

    [Test]
    public async Task RunAsync_ValidateServiceBusWithoutRoundTripValidator_DoesNotFallBackToSendOnlySamples()
    {
        var providers = new FakeProviders();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SampleApplication.RunAsync(["validate", "servicebus", "servicebus-topic", "--json"],
            () => new SampleRunner(providers, providers, providers), output, error);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(providers.Calls, Is.Empty);
        using var result = JsonDocument.Parse(output.ToString());
        Assert.That(result.RootElement.GetProperty("failed").GetInt32(), Is.EqualTo(2));
    }

    private sealed class FakeProviders : IResourceSamples, IMessagingSamples, IDatabaseSamples
    {
        public List<(string Resource, CancellationToken Token)> Calls { get; } = [];
        public Exception? Failure { get; init; }

        private Task<string> Result(string resource, CancellationToken token)
        {
            Calls.Add((resource, token));
            return Failure is null ? Task.FromResult(resource + " result") : Task.FromException<string>(Failure);
        }

        private Task<DatabaseSampleResult> Database(string resource, string text, CancellationToken token)
        {
            Calls.Add((resource, token));
            var message = new SampleMessage { Id = "sample-id", Text = text, Source = "database-sample" };
            return Failure is null ? Task.FromResult(new DatabaseSampleResult(resource, message, [message]))
                : Task.FromException<DatabaseSampleResult>(Failure);
        }

        public Task<string> CosmosAsync(CancellationToken cancellationToken = default) => Result("cosmos", cancellationToken);
        public Task<string> DynamoDbAsync(CancellationToken cancellationToken = default) => Result("dynamodb", cancellationToken);
        public Task<string> RedisAsync(CancellationToken cancellationToken = default) => Result("redis", cancellationToken);
        public Task<string> MailAsync(CancellationToken cancellationToken = default) => Result("mail", cancellationToken);
        public Task<string> SeqAsync(CancellationToken cancellationToken = default) => Result("seq", cancellationToken);
        public Task<string> WireMockAsync(CancellationToken cancellationToken = default) => Result("wiremock", cancellationToken);
        public Task<string> BlobAsync(CancellationToken cancellationToken = default) => Result("blob", cancellationToken);
        public Task<string> StorageQueueAsync(CancellationToken cancellationToken = default) => Result("storage-queue", cancellationToken);
        public Task<string> TableAsync(CancellationToken cancellationToken = default) => Result("table", cancellationToken);
        public Task<string> S3Async(CancellationToken cancellationToken = default) => Result("s3", cancellationToken);
        public Task<string> RabbitMqAsync(CancellationToken cancellationToken = default) => Result("rabbitmq", cancellationToken);
        public Task<string> SqsAsync(CancellationToken cancellationToken = default) => Result("sqs", cancellationToken);
        public Task<string> SnsAsync(CancellationToken cancellationToken = default) => Result("sns", cancellationToken);
        public Task<string> EventBridgeAsync(CancellationToken cancellationToken = default) => Result("eventbridge", cancellationToken);
        public Task<string> ServiceBusAsync(CancellationToken cancellationToken = default) => Result("servicebus", cancellationToken);
        public Task<string> ServiceBusTopicAsync(CancellationToken cancellationToken = default) => Result("servicebus-topic", cancellationToken);
        public Task<DatabaseSampleResult> SqlServerAsync(string text = "Hello SQL Server", CancellationToken cancellationToken = default) => Database("sqlserver", text, cancellationToken);
        public Task<DatabaseSampleResult> PostgresAsync(string text = "Hello PostgreSQL", CancellationToken cancellationToken = default) => Database("postgres", text, cancellationToken);
        public Task<bool> SaveSqlServerMessageAsync(string messageId, string text, string source, CancellationToken cancellationToken = default) => throw new InvalidOperationException("CLI must use the transaction example.");
        public Task<bool> SavePostgresMessageAsync(string messageId, string text, string source, CancellationToken cancellationToken = default) => throw new InvalidOperationException("CLI must use the transaction example.");
    }
}
