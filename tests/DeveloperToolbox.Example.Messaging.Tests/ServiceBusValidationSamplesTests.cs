using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace DeveloperToolbox.Example.Messaging.Tests;

public sealed class ServiceBusValidationSamplesTests
{
    [TestCase(false, "queue.validation", null)]
    [TestCase(true, "topic.validation", "subscription.validation")]
    public async Task ValidateAsync_CorrelatedDelivery_CompletesOnlyOwnedMessage(bool topic, string entity, string? subscription)
    {
        var client = new FakeClient();
        var sample = Create(client);
        var result = await Invoke(sample, topic);

        Assert.That(client.SenderEntity, Is.EqualTo(entity));
        Assert.That(client.ReceiverEntity, Is.EqualTo(entity));
        Assert.That(client.Subscription, Is.EqualTo(subscription));
        Assert.That(client.Mode, Is.EqualTo(ServiceBusReceiveMode.PeekLock));
        Assert.That(client.Receiver.Completed.Single(), Is.EqualTo(client.Sender.Sent!.MessageId));
        Assert.That(client.Receiver.Abandoned, Is.EqualTo(new[] { "unrelated-before", "unrelated-after" }));
        Assert.That(client.Operations.IndexOf("complete") < client.Operations.IndexOf("abandon-unrelated-before"), Is.True);
        foreach (var token in client.Receiver.CleanupTokens)
        {
            Assert.That(token.CanBeCanceled, Is.True);
        }
        Assert.That(client.Receiver.CloseToken.CanBeCanceled, Is.True);
        Assert.That(result, Is.EqualTo(topic ? "Validated Service Bus topic delivery." : "Validated Service Bus queue delivery."));
    }

    [TestCase("localhost:5672")]
    [TestCase("127.0.0.1:10170")]
    [TestCase("service-bus:5672")]
    public async Task ValidateQueueAsync_LocalEndpoint_PreservesCustomTransportAddress(string endpoint)
    {
        var client = new FakeClient();
        await Create(client, Connection(endpoint)).ValidateQueueAsync();

        Assert.That(client.Options!.CustomEndpointAddress, Is.EqualTo(new Uri("sb://" + endpoint)));
        Assert.That(client.Options.RetryOptions.MaxRetries, Is.EqualTo(0));
    }

    [Test]
    public async Task ValidateQueueAsync_UnmatchedWork_RetainsLocksUntilLaterCorrelatedDelivery()
    {
        var client = new FakeClient();
        client.Receiver.DelayCorrelation = true;
        await Create(client).ValidateQueueAsync();

        Assert.That(client.Operations, Is.EqualTo(new[] { "send", "receive-1", "receive-2", "complete", "abandon-unrelated-before", "abandon-unrelated-after" }));
    }

    [Test]
    public async Task ValidateQueueAsync_NoCorrelation_TimesOutAndAbandonsUnmatchedWork()
    {
        var client = new FakeClient();
        client.Receiver.ReturnCorrelation = false;
        await Assert.ThrowsAsync<TimeoutException>(() => Create(client).ValidateQueueAsync());

        Assert.That(client.Receiver.ReceiveCount, Is.EqualTo(5));
        Assert.That(client.Receiver.Completed, Is.Empty);
        Assert.That(client.Receiver.Abandoned, Is.EqualTo(new[] { "unrelated-before" }));
        Assert.That(client.Operations.Last(), Is.EqualTo("abandon-unrelated-before"));
    }

    [Test]
    public async Task ValidateQueueAsync_CanceledPolling_UsesIndependentCleanupToken()
    {
        using var caller = new CancellationTokenSource();
        var client = new FakeClient();
        client.Receiver.DelayCorrelation = true;
        client.Receiver.PollingFailure = new OperationCanceledException(caller.Token);
        var failure = (await Assert.ThrowsAsync<OperationCanceledException>(() => Create(client).ValidateQueueAsync(caller.Token)))!;

        Assert.That(failure.CancellationToken, Is.EqualTo(caller.Token));
        Assert.That(client.Receiver.Abandoned, Is.EqualTo(new[] { "unrelated-before" }));
        Assert.That(client.Receiver.CleanupTokens.Single(), Is.Not.EqualTo(caller.Token));
    }

    [Test]
    public async Task ValidateTopicAsync_CompletionFails_PropagatesProviderFailureAndAbandonsUnrelatedWork()
    {
        var client = new FakeClient();
        var expected = new InvalidOperationException("Fixture completion failure.");
        client.Receiver.CompletionFailure = expected;
        var actual = (await Assert.ThrowsAsync<InvalidOperationException>(() => Create(client).ValidateTopicAsync()))!;

        Assert.That(actual, Is.SameAs(expected));
        Assert.That(client.Receiver.Completed, Is.Empty);
        Assert.That(client.Receiver.Abandoned, Is.EqualTo(new[] { "unrelated-before", "unrelated-after" }));
    }

    [Test]
    public async Task ValidateQueueAsync_PollingAndCleanupFail_ReportsBothFailures()
    {
        var client = new FakeClient();
        client.Receiver.DelayCorrelation = true;
        var polling = new InvalidOperationException("Fixture polling failure.");
        var cleanup = new InvalidOperationException("Fixture cleanup failure.");
        client.Receiver.PollingFailure = polling;
        client.Receiver.CleanupFailure = cleanup;
        var failure = (await Assert.ThrowsAsync<AggregateException>(() => Create(client).ValidateQueueAsync()))!;

        Assert.That(failure.InnerExceptions, Is.EqualTo(new Exception[] { polling, cleanup }));
    }

    [Test]
    public async Task ValidateQueueAsync_CleanupFailsAfterCompletion_ReportsFailure()
    {
        var client = new FakeClient();
        var expected = new InvalidOperationException("Fixture cleanup failure.");
        client.Receiver.CleanupFailure = expected;
        var actual = (await Assert.ThrowsAsync<InvalidOperationException>(() => Create(client).ValidateQueueAsync()))!;

        Assert.That(actual, Is.SameAs(expected));
        Assert.That(client.Receiver.Completed, Has.Exactly(1).Items);
        Assert.That(client.Receiver.Abandoned.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ValidateQueueAsync_ReceiverCloseFails_ReportsFailureAfterAbandoningUnrelatedWork()
    {
        var client = new FakeClient();
        var expected = new InvalidOperationException("Fixture receiver close failure.");
        client.Receiver.CloseFailure = expected;
        var actual = (await Assert.ThrowsAsync<InvalidOperationException>(() => Create(client).ValidateQueueAsync()))!;

        Assert.That(actual, Is.SameAs(expected));
        Assert.That(client.Receiver.Abandoned.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ValidateQueueAsync_SendAndReceiverCloseFail_PreservesBothFailures()
    {
        var client = new FakeClient();
        var sending = new InvalidOperationException("Fixture sending failure.");
        var closing = new InvalidOperationException("Fixture receiver close failure.");
        client.Sender.Failure = sending;
        client.Receiver.CloseFailure = closing;
        var actual = (await Assert.ThrowsAsync<AggregateException>(() => Create(client).ValidateQueueAsync()))!;

        Assert.That(actual.InnerExceptions, Is.EqualTo(new Exception[] { sending, closing }));
    }

    [Test]
    public async Task ValidateQueueAsync_SendFails_PropagatesWithoutPolling()
    {
        var client = new FakeClient();
        var expected = new InvalidOperationException("Fixture send failure.");
        client.Sender.Failure = expected;
        var actual = (await Assert.ThrowsAsync<InvalidOperationException>(() => Create(client).ValidateQueueAsync()))!;

        Assert.That(actual, Is.SameAs(expected));
        Assert.That(client.Receiver.ReceiveCount, Is.EqualTo(0));
        Assert.That(client.Receiver.CloseToken.CanBeCanceled, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ValidateAsync_PreCanceledRequest_StopsBeforeCreatingClient(bool topic)
    {
        var client = new FakeClient();
        var failure = (await Assert.ThrowsAsync<OperationCanceledException>(() => Invoke(Create(client), topic, new CancellationToken(true))))!;

        Assert.That(failure.CancellationToken.IsCancellationRequested, Is.True);
        Assert.That(client.Options, Is.Null);
        Assert.That(client.Operations, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture;")]
    [TestCase("Endpoint=sb://remote.example/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture;UseDevelopmentEmulator=true;")]
    public async Task ValidateQueueAsync_InvalidConnection_RejectsBeforeCreatingClient(string? connection)
    {
        var client = new FakeClient();
        var sample = new ServiceBusValidationSamples(Configuration(connection), (_, options) => { client.Options = options; return client; });
        await Assert.ThrowsAsync<InvalidOperationException>(() => sample.ValidateQueueAsync());

        Assert.That(client.Options, Is.Null);
    }

    [Test]
    public async Task ValidateTopicAsync_MalformedConnection_RejectsBeforeCreatingClient()
    {
        var client = new FakeClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(client, "UseDevelopmentEmulator=true;invalid").ValidateTopicAsync());
        Assert.That(client.Options, Is.Null);
    }

    // Use one configuration contract for production and fake SDK clients.
    private static IConfiguration Configuration(string? connection) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceBusConnection"] = connection }).Build();

    private static string Connection(string endpoint) => $"Endpoint=sb://{endpoint}/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture;UseDevelopmentEmulator=true;";

    private static ServiceBusValidationSamples Create(FakeClient client, string? connection = null) =>
        new(Configuration(connection ?? Connection("localhost:10170")), (_, options) => { client.Options = options; return client; });

    private static Task<string> Invoke(ServiceBusValidationSamples sample, bool topic, CancellationToken token = default) =>
        topic ? sample.ValidateTopicAsync(token) : sample.ValidateQueueAsync(token);

    // Protected SDK mock constructors avoid all real connections and authentication.
    private sealed class FakeClient : ServiceBusClient
    {
        public List<string> Operations { get; } = [];
        public FakeSender Sender { get; }
        public FakeReceiver Receiver { get; }
        public ServiceBusClientOptions? Options { get; set; }
        public string? SenderEntity { get; private set; }
        public string? ReceiverEntity { get; private set; }
        public string? Subscription { get; private set; }
        public ServiceBusReceiveMode Mode { get; private set; }

        public FakeClient()
        {
            Sender = new(Operations);
            Receiver = new(Sender, Operations);
        }

        public override ServiceBusSender CreateSender(string queueOrTopicName)
        {
            SenderEntity = queueOrTopicName;
            return Sender;
        }

        public override ServiceBusReceiver CreateReceiver(string queueName, ServiceBusReceiverOptions options)
        {
            ReceiverEntity = queueName;
            Mode = options.ReceiveMode;
            return Receiver;
        }

        public override ServiceBusReceiver CreateReceiver(string topicName, string subscriptionName, ServiceBusReceiverOptions options)
        {
            ReceiverEntity = topicName;
            Subscription = subscriptionName;
            Mode = options.ReceiveMode;
            return Receiver;
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSender(List<string> operations) : ServiceBusSender
    {
        public ServiceBusMessage? Sent { get; private set; }
        public Exception? Failure { get; set; }

        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        {
            Sent = message;
            operations.Add("send");
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeReceiver(FakeSender sender, List<string> operations) : ServiceBusReceiver
    {
        public bool ReturnCorrelation { get; set; } = true;
        public bool DelayCorrelation { get; set; }
        public Exception? PollingFailure { get; set; }
        public Exception? CompletionFailure { get; set; }
        public Exception? CleanupFailure { get; set; }
        public Exception? CloseFailure { get; set; }
        public int ReceiveCount { get; private set; }
        public List<string> Completed { get; } = [];
        public List<string> Abandoned { get; } = [];
        public List<CancellationToken> CleanupTokens { get; } = [];
        public CancellationToken CloseToken { get; private set; }

        public override Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveMessagesAsync(int maxMessages, TimeSpan? maxWaitTime = default, CancellationToken cancellationToken = default)
        {
            ReceiveCount++;
            operations.Add("receive-" + ReceiveCount);
            if (ReceiveCount > 1 && PollingFailure is not null)
            {
                return Task.FromException<IReadOnlyList<ServiceBusReceivedMessage>>(PollingFailure);
            }
            var result = new List<ServiceBusReceivedMessage>();
            if (ReceiveCount == 1) { result.Add(Message("unrelated-before")); }
            if (ReturnCorrelation && (!DelayCorrelation || ReceiveCount > 1))
            {
                result.Add(Message(sender.Sent!.MessageId));
                result.Add(Message("unrelated-after"));
            }
            return Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(result);
        }

        public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
        {
            operations.Add("complete");
            if (CompletionFailure is not null) { return Task.FromException(CompletionFailure); }
            Completed.Add(message.MessageId);
            return Task.CompletedTask;
        }

        public override Task AbandonMessageAsync(ServiceBusReceivedMessage message, IDictionary<string, object>? propertiesToModify = null, CancellationToken cancellationToken = default)
        {
            Abandoned.Add(message.MessageId);
            CleanupTokens.Add(cancellationToken);
            operations.Add("abandon-" + message.MessageId);
            return CleanupFailure is null ? Task.CompletedTask : Task.FromException(CleanupFailure);
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public override Task CloseAsync(CancellationToken cancellationToken = default)
        {
            CloseToken = cancellationToken;
            return CloseFailure is null ? Task.CompletedTask : Task.FromException(CloseFailure);
        }

        private static ServiceBusReceivedMessage Message(string id) => ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("Fixture body does not determine correlation."), messageId: id, lockTokenGuid: Guid.NewGuid());
    }
}
