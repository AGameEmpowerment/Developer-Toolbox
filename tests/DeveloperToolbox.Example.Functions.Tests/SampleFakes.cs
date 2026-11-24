using DeveloperToolbox.Example.Data;
using DeveloperToolbox.Example.Messaging;
using DeveloperToolbox.Example.Resources;
using Microsoft.Extensions.Logging;

namespace DeveloperToolbox.Example.Functions.Tests;

internal sealed record SampleCall(string Name, CancellationToken CancellationToken, string? Id = null, string? Text = null, string? Source = null);

internal sealed class SampleFakes : IDatabaseSamples, IResourceSamples, IMessagingSamples, IQueuePublishers
{
    public List<SampleCall> Calls { get; } = [];
    public Exception? Failure { get; set; }
    public bool Inserted { get; set; } = true;

    private void Record(SampleCall call)
    {
        Calls.Add(call);
        if (Failure is not null)
        {
            throw Failure;
        }
        call.CancellationToken.ThrowIfCancellationRequested();
    }

    private Task<string> Sample(string name, CancellationToken token)
    {
        Record(new SampleCall(name, token));
        return Task.FromResult($"result:{name}");
    }

    private Task<DatabaseSampleResult> Database(string name, string text, CancellationToken token)
    {
        Record(new SampleCall(name, token, Text: text));
        var message = new SampleMessage { Id = "sample-id", Text = text, Source = "test", CreatedAtUtc = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc) };
        return Task.FromResult(new DatabaseSampleResult(name, message, [message]));
    }

    private Task<bool> Save(string name, string id, string text, string source, CancellationToken token)
    {
        Record(new SampleCall(name, token, id, text, source));
        return Task.FromResult(Inserted);
    }

    public Task<DatabaseSampleResult> SqlServerAsync(string text = "Hello SQL Server", CancellationToken cancellationToken = default) => Database("sqlserver", text, cancellationToken);
    public Task<DatabaseSampleResult> PostgresAsync(string text = "Hello PostgreSQL", CancellationToken cancellationToken = default) => Database("postgres", text, cancellationToken);
    public Task<bool> SaveSqlServerMessageAsync(string messageId, string text, string source, CancellationToken cancellationToken = default) => Save("sqlserver-save", messageId, text, source, cancellationToken);
    public Task<bool> SavePostgresMessageAsync(string messageId, string text, string source, CancellationToken cancellationToken = default) => Save("postgres-save", messageId, text, source, cancellationToken);
    public Task<string> CosmosAsync(CancellationToken cancellationToken = default) => Sample("cosmos", cancellationToken);
    public Task<string> DynamoDbAsync(CancellationToken cancellationToken = default) => Sample("dynamodb", cancellationToken);
    public Task<string> RedisAsync(CancellationToken cancellationToken = default) => Sample("redis", cancellationToken);
    public Task<string> MailAsync(CancellationToken cancellationToken = default) => Sample("mail", cancellationToken);
    public Task<string> SeqAsync(CancellationToken cancellationToken = default) => Sample("seq", cancellationToken);
    public Task<string> WireMockAsync(CancellationToken cancellationToken = default) => Sample("wiremock", cancellationToken);
    public Task<string> BlobAsync(CancellationToken cancellationToken = default) => Sample("blob", cancellationToken);
    public Task<string> StorageQueueAsync(CancellationToken cancellationToken = default) => Sample("storage-queue", cancellationToken);
    public Task<string> TableAsync(CancellationToken cancellationToken = default) => Sample("table", cancellationToken);
    public Task<string> S3Async(CancellationToken cancellationToken = default) => Sample("s3", cancellationToken);
    public Task<string> RabbitMqAsync(CancellationToken cancellationToken = default) => Sample("rabbitmq", cancellationToken);
    public Task<string> SqsAsync(CancellationToken cancellationToken = default) => Sample("sqs", cancellationToken);
    public Task<string> SnsAsync(CancellationToken cancellationToken = default) => Sample("sns", cancellationToken);
    public Task<string> EventBridgeAsync(CancellationToken cancellationToken = default) => Sample("eventbridge", cancellationToken);
    public Task<string> ServiceBusAsync(CancellationToken cancellationToken = default) => Sample("messaging-servicebus", cancellationToken);
    public Task<string> ServiceBusTopicAsync(CancellationToken cancellationToken = default) => Sample("messaging-servicebus-topic", cancellationToken);

    public Task<QueueWorkItem> ServiceBusAsync(string text, bool topic, CancellationToken cancellationToken)
    {
        Record(new SampleCall(topic ? "servicebus-topic" : "servicebus-queue", cancellationToken, Text: text));
        return Task.FromResult(new QueueWorkItem("queue-id", text));
    }

    public Task<QueueWorkItem> StorageQueueAsync(string text, CancellationToken cancellationToken)
    {
        Record(new SampleCall("function-storage-queue", cancellationToken, Text: text));
        return Task.FromResult(new QueueWorkItem("queue-id", text));
    }
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<IReadOnlyDictionary<string, object?>> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(pair => pair.Key, pair => pair.Value));
    }
}
