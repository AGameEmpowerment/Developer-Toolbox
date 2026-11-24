using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace DeveloperToolbox.Example.Messaging.Tests;

public sealed class MessagingSamplesTests
{
    [Test]
    public async Task SqsAsync_ValidRequest_SendsAndDeletesOnlyCorrelatedMessage()
    {
        using var providers = new FakeProviders();
        using var sample = providers.CreateSample();
        var body = await sample.SqsAsync();

        using var json = JsonDocument.Parse(body);
        Assert.That(json.RootElement.GetProperty("message").GetString(), Is.EqualTo("Hello SQS"));
        Assert.That(Guid.TryParse(json.RootElement.GetProperty("id").GetString(), out _), Is.True);
        Assert.That(providers.Sqs.Queues.Last(), Is.EqualTo("toolbox-work"));
        Assert.That(providers.Sqs.Deleted, Is.EqualTo(new[] { "correlated" }));
        Assert.That(providers.Sqs.Restored, Is.EqualTo(new[] { "unrelated" }));
    }

    [Test]
    public async Task SnsAsync_ValidRequest_PublishesAndReceivesSubscriptionDelivery()
    {
        using var providers = new FakeProviders();
        using var sample = providers.CreateSample();
        var body = await sample.SnsAsync();

        Assert.That(providers.Sns.TopicName, Is.EqualTo("toolbox-notifications"));
        Assert.That(providers.Sns.Published!.TopicArn, Is.EqualTo("arn:aws:sns:us-east-1:000000000000:toolbox-notifications"));
        Assert.That(providers.Sns.Published.Message, Is.EqualTo(body));
        Assert.That(providers.Sqs.Queues.Last(), Is.EqualTo("toolbox-subscriber"));
    }

    [Test]
    public async Task EventBridgeAsync_ValidRequest_PublishesContractAndReceivesEnvelope()
    {
        using var providers = new FakeProviders();
        using var sample = providers.CreateSample();
        var body = await sample.EventBridgeAsync();

        var entry = providers.Events.Request!.Entries.Single();
        Assert.That(entry.EventBusName, Is.EqualTo("toolbox"));
        Assert.That(entry.Source, Is.EqualTo("developertoolbox"));
        Assert.That(entry.DetailType, Is.EqualTo("ToolboxSample"));
        using var envelope = JsonDocument.Parse(body);
        Assert.That(envelope.RootElement.GetProperty("detail").GetProperty("message").GetString(), Is.EqualTo("Hello EventBridge"));
        Assert.That(providers.Sqs.Queues.Last(), Is.EqualTo("toolbox-events"));
    }

    [Test]
    public async Task EventBridgeAsync_RejectedEntry_StopsBeforeReceivingAndReturnsSafeError()
    {
        using var providers = new FakeProviders();
        providers.Events.RejectEntry = true;
        using var sample = providers.CreateSample();
        var failure = (await Assert.ThrowsAsync<InvalidOperationException>(() => sample.EventBridgeAsync()))!;

        Assert.That(failure.Message, Is.EqualTo("EventBridge rejected the sample event."));
        Assert.That(providers.Sqs.Queues, Is.Empty);
    }

    [TestCase("sqs")]
    [TestCase("sns")]
    [TestCase("eventbridge")]
    public async Task AwsSample_ProviderFailure_PreservesProviderException(string technology)
    {
        using var providers = new FakeProviders();
        var expected = new InvalidOperationException("Fixture provider failure.");
        providers.Sqs.SendFailure = expected;
        providers.Sns.Failure = expected;
        providers.Events.Failure = expected;
        using var sample = providers.CreateSample();

        var failure = (await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(sample, technology)))!;
        Assert.That(failure, Is.SameAs(expected));
        Assert.That(providers.Sqs.Deleted, Is.Empty);
    }

    [TestCase("sqs", "https://sqs.us-east-1.amazonaws.com")]
    [TestCase("sns", "https://sns.us-east-1.amazonaws.com")]
    [TestCase("eventbridge", "https://events.us-east-1.amazonaws.com")]
    [TestCase("sqs", "not a URL")]
    [TestCase("sns", "file://localhost/")]
    [TestCase("eventbridge", "http://user:password@localhost:10195")]
    [TestCase("sqs", "http://localhost:10195/path")]
    [TestCase("sns", "http://localhost:10195?token=fixture")]
    [TestCase("eventbridge", "http://localhost:10195#fragment")]
    public async Task AwsSample_InvalidEndpoint_RejectsBeforeProviderAccess(string technology, string endpoint)
    {
        using var providers = new FakeProviders();
        using var sample = providers.CreateSample(new() { ["TOOLBOX_AWS_ENDPOINT"] = endpoint });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(sample, technology));

        Assert.That(providers.Sqs.Queues, Is.Empty);
        Assert.That(providers.Sns.TopicName, Is.Null);
        Assert.That(providers.Events.Request, Is.Null);
    }

    [TestCase("sqs")]
    [TestCase("sns")]
    [TestCase("eventbridge")]
    public async Task AwsSample_ComposeEndpoint_UsesCanonicalEmulatorHost(string technology)
    {
        using var providers = new FakeProviders();
        using var sample = providers.CreateSample(new() { ["TOOLBOX_AWS_ENDPOINT"] = "http://aws-emulator:5000" });
        Assert.That(await Invoke(sample, technology), Is.Not.Empty);
    }

    [TestCase("sqs")]
    [TestCase("sns")]
    [TestCase("eventbridge")]
    [TestCase("rabbitmq")]
    [TestCase("servicebus")]
    [TestCase("servicebus-topic")]
    public async Task Sample_CanceledRequest_StopsBeforeProviderAccess(string technology)
    {
        using var providers = new FakeProviders();
        using var sample = providers.CreateSample(new()
        {
            ["RABBITMQ_DEFAULT_PASS"] = "fixture-only",
            ["ServiceBusConnection"] = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture;UseDevelopmentEmulator=true;"
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = (await Assert.ThrowsAsync<OperationCanceledException>(() => Invoke(sample, technology, cancellation.Token)))!;

        Assert.That(failure.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(providers.Sqs.Queues, Is.Empty);
        Assert.That(providers.Sns.TopicName, Is.Null);
        Assert.That(providers.Events.Request, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task RabbitMqAsync_MissingPassword_RejectsBeforeConnecting(string? password)
    {
        using var providers = new FakeProviders();
        using var sample = providers.CreateSample(new() { ["RABBITMQ_DEFAULT_PASS"] = password });
        await Assert.ThrowsAsync<InvalidOperationException>(() => sample.RabbitMqAsync());
    }

    [TestCase("remote.example", "5672")]
    [TestCase("rabbitmq", "0")]
    [TestCase("localhost", "65536")]
    [TestCase("localhost", "not a port")]
    public async Task RabbitMqAsync_InvalidDestination_RejectsBeforeConnecting(string host, string port)
    {
        using var providers = new FakeProviders();
        using var sample = providers.CreateSample(new()
        {
            ["RABBITMQ_HOST"] = host, ["RABBITMQ_PORT"] = port, ["RABBITMQ_DEFAULT_PASS"] = "fixture-only"
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => sample.RabbitMqAsync());
    }

    [TestCase("rabbitmq")]
    [TestCase("servicebus")]
    [TestCase("servicebus-topic")]
    public async Task NativeSample_ComposeDestinationAndCancellation_ValidatesBeforeConnecting(string technology)
    {
        using var providers = new FakeProviders();
        using var sample = providers.CreateSample(new()
        {
            ["RABBITMQ_HOST"] = "rabbitmq", ["RABBITMQ_PORT"] = "5672", ["RABBITMQ_DEFAULT_PASS"] = "fixture-only",
            ["ServiceBusConnection"] = "Endpoint=sb://service-bus/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture;UseDevelopmentEmulator=true;"
        });
        await Assert.ThrowsAsync<OperationCanceledException>(() => Invoke(sample, technology, new CancellationToken(true)));
    }

    [TestCase("servicebus", null)]
    [TestCase("servicebus-topic", null)]
    [TestCase("servicebus", "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture;")]
    [TestCase("servicebus-topic", "Endpoint=sb://remote.example/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture;UseDevelopmentEmulator=true;")]
    public async Task ServiceBusSample_InvalidConnection_RejectsBeforeConnecting(string technology, string? connection)
    {
        using var providers = new FakeProviders();
        using var sample = providers.CreateSample(new() { ["ServiceBusConnection"] = connection });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(sample, technology));
    }

    [TestCase("servicebus")]
    [TestCase("servicebus-topic")]
    public async Task ServiceBusSample_MalformedConnection_RejectsBeforeConnecting(string technology)
    {
        using var providers = new FakeProviders();
        using var sample = providers.CreateSample(new() { ["ServiceBusConnection"] = "UseDevelopmentEmulator=true;invalid" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(sample, technology));
    }

    // Each case invokes the public contract used by the app and Functions host.
    private static Task<string> Invoke(IMessagingSamples sample, string technology, CancellationToken token = default) => technology switch
    {
        "sqs" => sample.SqsAsync(token),
        "sns" => sample.SnsAsync(token),
        "eventbridge" => sample.EventBridgeAsync(token),
        "rabbitmq" => sample.RabbitMqAsync(token),
        "servicebus" => sample.ServiceBusAsync(token),
        "servicebus-topic" => sample.ServiceBusTopicAsync(token),
        _ => throw new ArgumentException("Unknown test technology.", nameof(technology))
    };

    // SDK subclasses override every used operation; their constructors never issue requests.
    private sealed class FakeProviders : IDisposable
    {
        public WorkflowSqs Sqs { get; } = new();
        public WorkflowSns Sns { get; }
        public WorkflowEvents Events { get; }

        public FakeProviders()
        {
            Sns = new(body => Sqs.Body = body);
            Events = new(body => Sqs.Body = body);
        }

        public MessagingSamples CreateSample(Dictionary<string, string?>? settings = null) =>
            new(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build(), Sqs, Sns, Events);

        public void Dispose()
        {
            Sqs.Dispose();
            Sns.Dispose();
            Events.Dispose();
        }
    }

    private sealed class WorkflowSqs : AmazonSQSClient
    {
        public string Body { get; set; } = "";
        public Exception? SendFailure { get; set; }
        public List<string> Queues { get; } = [];
        public List<string> Deleted { get; } = [];
        public List<string> Restored { get; } = [];

        public WorkflowSqs() : base(new BasicAWSCredentials("test", "test"), new AmazonSQSConfig
        { ServiceURL = "http://localhost:10195", AuthenticationRegion = "us-east-1" }) { }

        public override Task<GetQueueUrlResponse> GetQueueUrlAsync(string queueName, CancellationToken cancellationToken = default)
        {
            Queues.Add(queueName);
            return Task.FromResult(new GetQueueUrlResponse { QueueUrl = "http://localhost:10195/" + queueName });
        }

        public override Task<SendMessageResponse> SendMessageAsync(string queueUrl, string messageBody, CancellationToken cancellationToken = default)
        {
            Body = messageBody;
            return SendFailure is null ? Task.FromResult(new SendMessageResponse()) : Task.FromException<SendMessageResponse>(SendFailure);
        }

        public override Task<ReceiveMessageResponse> ReceiveMessageAsync(ReceiveMessageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReceiveMessageResponse
            { Messages = [new() { Body = "unrelated work", ReceiptHandle = "unrelated" }, new() { Body = Body, ReceiptHandle = "correlated" }] });

        public override Task<DeleteMessageResponse> DeleteMessageAsync(string queueUrl, string receiptHandle, CancellationToken cancellationToken = default)
        {
            Deleted.Add(receiptHandle);
            return Task.FromResult(new DeleteMessageResponse());
        }

        public override Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(string queueUrl, string receiptHandle, int? visibilityTimeout, CancellationToken cancellationToken = default)
        {
            Assert.That(visibilityTimeout, Is.EqualTo(0));
            Restored.Add(receiptHandle);
            return Task.FromResult(new ChangeMessageVisibilityResponse());
        }
    }

    private sealed class WorkflowSns(Action<string> deliver) : AmazonSimpleNotificationServiceClient(
        new BasicAWSCredentials("test", "test"), new AmazonSimpleNotificationServiceConfig
        { ServiceURL = "http://localhost:10195", AuthenticationRegion = "us-east-1" })
    {
        public string? TopicName { get; private set; }
        public PublishRequest? Published { get; private set; }
        public Exception? Failure { get; set; }

        public override Task<CreateTopicResponse> CreateTopicAsync(string name, CancellationToken cancellationToken = default)
        {
            TopicName = name;
            return Task.FromResult(new CreateTopicResponse { TopicArn = "arn:aws:sns:us-east-1:000000000000:" + name });
        }

        public override Task<PublishResponse> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default)
        {
            Published = request;
            deliver(request.Message);
            return Failure is null ? Task.FromResult(new PublishResponse()) : Task.FromException<PublishResponse>(Failure);
        }
    }

    private sealed class WorkflowEvents(Action<string> deliver) : AmazonEventBridgeClient(
        new BasicAWSCredentials("test", "test"), new AmazonEventBridgeConfig
        { ServiceURL = "http://localhost:10195", AuthenticationRegion = "us-east-1" })
    {
        public PutEventsRequest? Request { get; private set; }
        public bool RejectEntry { get; set; }
        public Exception? Failure { get; set; }

        public override Task<PutEventsResponse> PutEventsAsync(PutEventsRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            deliver("{\"detail\":" + request.Entries[0].Detail + "}");
            return Failure is null ? Task.FromResult(new PutEventsResponse { FailedEntryCount = RejectEntry ? 1 : 0 }) :
                Task.FromException<PutEventsResponse>(Failure);
        }
    }
}
