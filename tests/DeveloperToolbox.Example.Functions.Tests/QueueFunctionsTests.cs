using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues.Models;

namespace DeveloperToolbox.Example.Functions.Tests;

public sealed class QueueFunctionsTests
{
    [TestCase("queue", "sqlserver-save", "servicebus-queue", true)]
    [TestCase("queue", "sqlserver-save", "servicebus-queue", false)]
    [TestCase("topic", "postgres-save", "servicebus-topic", true)]
    [TestCase("topic", "postgres-save", "servicebus-topic", false)]
    [TestCase("storage", "postgres-save", "azurite-queue", true)]
    [TestCase("storage", "postgres-save", "azurite-queue", false)]
    public async Task Trigger_ValidOrRepeatedMessage_PersistsAndLogsOutcome(string kind, string expectedDatabase, string expectedSource, bool inserted)
    {
        var fake = new SampleFakes { Inserted = inserted };
        var logger = new RecordingLogger<QueueFunctions>();
        using var cancellation = new CancellationTokenSource();
        await InvokeAsync(new QueueFunctions(fake, logger), kind, "{\"id\":\"stable-id\",\"text\":\"sample work\"}", cancellation.Token);
        Assert.That(fake.Calls, Has.Exactly(1).Items);
        var call = fake.Calls.Single();
        Assert.That(call, Is.EqualTo(new SampleCall(expectedDatabase, cancellation.Token, "stable-id", "sample work", expectedSource)));
        Assert.That(logger.Entries, Has.Exactly(1).Items);
        var entry = logger.Entries.Single();
        Assert.That(entry["MessageId"], Is.EqualTo("stable-id"));
        Assert.That(entry["Inserted"], Is.EqualTo(inserted));
    }

    public static IEnumerable<object[]> InvalidEnvelopes()
    {
        foreach (var kind in new[] { "queue", "topic", "storage" })
        {
            foreach (var body in new[] { "null", "{}", "{\"id\":null,\"text\":\"payload\"}", "{\"id\":\" \" ,\"text\":\"payload\"}", "{\"id\":\"id\",\"text\":null}", "{\"id\":\"id\",\"text\":\" \"}" })
            {
                yield return [kind, body];
            }
            yield return [kind, JsonSerializer.Serialize(new QueueWorkItem(new string('i', 129), "payload"))];
            yield return [kind, JsonSerializer.Serialize(new QueueWorkItem("id", new string('t', 4097)))];
        }
    }

    [TestCaseSource(nameof(InvalidEnvelopes))]
    public async Task Trigger_InvalidEnvelope_RejectsBeforeDatabaseWrite(string kind, string body)
    {
        var fake = new SampleFakes();
        var logger = new RecordingLogger<QueueFunctions>();
        await Assert.CatchAsync<ArgumentException>(() => InvokeAsync(new QueueFunctions(fake, logger), kind, body));
        Assert.That(fake.Calls, Is.Empty);
        Assert.That(logger.Entries, Is.Empty);
    }

    [TestCase("queue")]
    [TestCase("topic")]
    [TestCase("storage")]
    public async Task Trigger_MalformedJson_PropagatesParsingFailure(string kind)
    {
        var fake = new SampleFakes();
        await Assert.ThrowsAsync<JsonException>(() => InvokeAsync(new QueueFunctions(fake, new RecordingLogger<QueueFunctions>()), kind, "{broken"));
        Assert.That(fake.Calls, Is.Empty);
    }

    [TestCase("queue")]
    [TestCase("topic")]
    [TestCase("storage")]
    public async Task Trigger_BoundaryPayload_IsAccepted(string kind)
    {
        var fake = new SampleFakes();
        var work = new QueueWorkItem(new string('i', 128), new string('t', 4096));
        await InvokeAsync(new QueueFunctions(fake, new RecordingLogger<QueueFunctions>()), kind, JsonSerializer.Serialize(work));
        Assert.That(fake.Calls, Has.Exactly(1).Items);
        Assert.That(fake.Calls.Single().Text, Is.EqualTo(work.Text));
    }

    [TestCase("queue")]
    [TestCase("topic")]
    [TestCase("storage")]
    public async Task Trigger_DatabaseFailure_PropagatesWithoutSuccessLog(string kind)
    {
        var error = new IOException("database unavailable");
        var fake = new SampleFakes { Failure = error };
        var logger = new RecordingLogger<QueueFunctions>();
        var thrown = await Assert.ThrowsAsync<IOException>(() => InvokeAsync(new QueueFunctions(fake, logger), kind, "{\"Id\":\"id\",\"Text\":\"payload\"}"));
        Assert.That(thrown, Is.SameAs(error));
        Assert.That(logger.Entries, Is.Empty);
    }

    [TestCase("queue")]
    [TestCase("topic")]
    [TestCase("storage")]
    public async Task Trigger_CanceledInvocation_ForwardsCancellationAndDoesNotLogSuccess(string kind)
    {
        var fake = new SampleFakes();
        var logger = new RecordingLogger<QueueFunctions>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.CatchAsync<OperationCanceledException>(() => InvokeAsync(new QueueFunctions(fake, logger), kind, "{\"id\":\"id\",\"text\":\"payload\"}", cancellation.Token));
        Assert.That(fake.Calls, Has.Exactly(1).Items);
        Assert.That(fake.Calls.Single().CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(logger.Entries, Is.Empty);
    }

    [TestCase("queue")]
    [TestCase("topic")]
    [TestCase("storage")]
    public async Task Trigger_NullMessage_RejectsBeforeDatabaseWrite(string kind)
    {
        var fake = new SampleFakes();
        var functions = new QueueFunctions(fake, new RecordingLogger<QueueFunctions>());
        Func<Task> invoke = kind switch
        {
            "queue" => () => functions.ServiceBusQueueAsync(null!, default),
            "topic" => () => functions.ServiceBusTopicAsync(null!, default),
            _ => () => functions.StorageQueueAsync(null!, default)
        };
        await Assert.ThrowsAsync<ArgumentNullException>(invoke);
        Assert.That(fake.Calls, Is.Empty);
    }

    private static Task InvokeAsync(QueueFunctions functions, string kind, string body, CancellationToken token = default)
    {
        var data = BinaryData.FromString(body);
        return kind switch
        {
            "queue" => functions.ServiceBusQueueAsync(ServiceBusModelFactory.ServiceBusReceivedMessage(body: data, messageId: "broker-id"), token),
            "topic" => functions.ServiceBusTopicAsync(ServiceBusModelFactory.ServiceBusReceivedMessage(body: data, messageId: "broker-id"), token),
            _ => functions.StorageQueueAsync(QueuesModelFactory.QueueMessage("storage-id", "receipt", data, 1, null, null, null), token)
        };
    }
}
