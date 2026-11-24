using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Configuration;

namespace DeveloperToolbox.Example.Functions.Tests;

public sealed class QueuePublishersTests
{
    private const string LOCAL_BUS = "Endpoint=sb://localhost:10170;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    [TestCase(false, "queue.functions")]
    [TestCase(true, "topic.functions")]
    public async Task ServiceBusAsync_ValidWork_PublishesStableEnvelopeToDedicatedEntity(bool topic, string expectedEntity)
    {
        var client = new FakeBusClient();
        ServiceBusClientOptions? options = null;
        await using var publisher = CreatePublisher(Configuration(LOCAL_BUS), (connection, value) => { options = value; return client; });
        using var cancellation = new CancellationTokenSource();
        var work = await publisher.ServiceBusAsync("work item", topic, cancellation.Token);
        Assert.That(client.Entity, Is.EqualTo(expectedEntity));
        Assert.That(client.Sender.Token, Is.EqualTo(cancellation.Token));
        Assert.That(client.Sender.Message!.MessageId, Is.EqualTo(work.Id));
        Assert.That(client.Sender.Message.ContentType, Is.EqualTo("application/json"));
        Assert.That(client.Sender.Message.Body.ToObjectFromJson<QueueWorkItem>(), Is.EqualTo(work));
        Assert.That(Guid.TryParseExact(work.Id, "N", out _), Is.True);
        Assert.That(options!.CustomEndpointAddress!.Port, Is.EqualTo(10170));
        Assert.That(client.Sender.Disposed, Is.True);
    }

    [Test]
    public async Task ServiceBusAsync_ComposeHost_AcceptsOnlyConfiguredLocalEmulator()
    {
        var client = new FakeBusClient();
        await using var publisher = CreatePublisher(Configuration(LOCAL_BUS.Replace("localhost:10170", "service-bus")), (_, _) => client);
        await publisher.ServiceBusAsync("payload", false, default);
        Assert.That(client.Entity, Is.EqualTo("queue.functions"));
    }

    [Test]
    public async Task ServiceBusAsync_RepeatedCalls_ReusesClientAndCreatesIndependentMessageIds()
    {
        var client = new FakeBusClient();
        var creations = 0;
        var publisher = CreatePublisher(Configuration(LOCAL_BUS), (_, _) => { creations++; return client; });
        var first = await publisher.ServiceBusAsync("first", false, default);
        var second = await publisher.ServiceBusAsync("second", true, default);
        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
        Assert.That(creations, Is.EqualTo(1));
        await publisher.DisposeAsync();
        Assert.That(client.Disposed, Is.True);
    }

    [TestCase("UseDevelopmentEmulator = true")]
    [TestCase(" UseDevelopmentEmulator=true")]
    [TestCase("usedevelopmentemulator=true")]
    [TestCase("UseDevelopmentEmulator=true;UseDevelopmentEmulator=false")]
    [TestCase("UseDevelopmentEmulator=true;usedevelopmentemulator=false")]
    [TestCase("UseDevelopmentEmulator=true;Endpoint=sb://remote.example/")]
    [TestCase("UseDevelopmentEmulator=true;Endpoint=sb://localhost/")]
    [TestCase("UseDevelopmentEmulator=true;endpoint=sb://remote.example/")]
    [TestCase("UseDevelopmentEmulator=true; Endpoint=sb://remote.example/")]
    [TestCase("UseDevelopmentEmulator=true;Endpoint =sb://remote.example/")]
    [TestCase("UseDevelopmentEmulator=true;SharedAccessKey=another")]
    [TestCase("UseDevelopmentEmulator=true;SharedAccessKeyName=another")]
    [TestCase("UseDevelopmentEmulator=true;EntityPath=queue")]
    [TestCase("UseDevelopmentEmulator=true;malformed")]
    public async Task ServiceBusAsync_NoncanonicalOrDuplicateFields_RejectsBeforeFactoryUse(string fields)
    {
        var connection = $"Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture;{fields};";
        var creations = 0;
        await using var publisher = CreatePublisher(Configuration(connection), (_, _) => { creations++; return new FakeBusClient(); });

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.ServiceBusAsync("work", false, default));
        Assert.That(creations, Is.EqualTo(0));
    }

    [TestCase("sb://remote.example/")]
    [TestCase("https://localhost/")]
    [TestCase("sb://user:secret@localhost/")]
    [TestCase("sb://localhost/queue")]
    [TestCase("sb://localhost/?redirect=cloud")]
    [TestCase("sb://localhost/#fragment")]
    public async Task ServiceBusAsync_UnsafeEndpoint_RejectsBeforeFactoryUse(string endpoint)
    {
        var connection = $"Endpoint={endpoint};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture;UseDevelopmentEmulator=true;";
        var creations = 0;
        await using var publisher = CreatePublisher(Configuration(connection), (_, _) => { creations++; return new FakeBusClient(); });

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.ServiceBusAsync("work", false, default));
        Assert.That(creations, Is.EqualTo(0));
    }

    [TestCase("localhost:10170")]
    [TestCase("127.0.0.2:10170")]
    [TestCase("[::1]:10170")]
    [TestCase("service-bus:5672")]
    public async Task ServiceBusAsync_CanonicalLocalConnection_MatchesSdkSelectedEndpoint(string host)
    {
        var connection = $"Endpoint=sb://{host}/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture==;UseDevelopmentEmulator=true;";
        string? selectedConnection = null;
        ServiceBusClientOptions? selectedOptions = null;
        await using var publisher = CreatePublisher(Configuration(connection), (value, options) =>
        {
            selectedConnection = value;
            selectedOptions = options;
            return new FakeBusClient();
        });

        await publisher.ServiceBusAsync("work", false, default);

        Assert.That(selectedConnection, Is.EqualTo(connection));
        var expectedEndpoint = new Uri($"sb://{host}/");
        Assert.That(selectedOptions!.CustomEndpointAddress, Is.EqualTo(expectedEndpoint));
        // Construction parses the SDK destination without sending to the emulator.
        await using var sdkClient = new ServiceBusClient(selectedConnection, selectedOptions);
        Assert.That(sdkClient.FullyQualifiedNamespace, Is.EqualTo(expectedEndpoint.Host));
    }

    [TestCase("remote.example", "true")]
    [TestCase("localhost", "false")]
    [TestCase("localhost", "not-a-boolean")]
    public async Task ServiceBusAsync_DisallowedConfiguration_RejectsBeforeFactoryUse(string host, string emulator)
    {
        var creations = 0;
        var connection = $"Endpoint=sb://{host};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator={emulator};";
        await using var publisher = CreatePublisher(Configuration(connection), (_, _) => { creations++; return new FakeBusClient(); });
        await Assert.CatchAsync<Exception>(() => publisher.ServiceBusAsync("work", false, default));
        Assert.That(creations, Is.EqualTo(0));
    }

    [TestCase(null)]
    [TestCase("Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;")]
    public async Task ServiceBusAsync_MissingEmulatorConfiguration_RejectsWithoutNetworkCalls(string? connection)
    {
        await using var publisher = CreatePublisher(Configuration(connection), (_, _) => throw new AssertionException("Client factory must not run."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.ServiceBusAsync("work", false, default));
    }

    [TestCase("servicebus", null)]
    [TestCase("servicebus", "")]
    [TestCase("servicebus", " ")]
    [TestCase("storage", null)]
    [TestCase("storage", "")]
    [TestCase("storage", " ")]
    public async Task Publisher_EmptyText_RejectsBeforeCreatingClients(string kind, string? text)
    {
        await using var publisher = new QueuePublishers(new ConfigurationBuilder().Build());
        await Assert.CatchAsync<ArgumentException>(() => kind == "servicebus"
            ? publisher.ServiceBusAsync(text!, false, default)
            : publisher.StorageQueueAsync(text!, default));
    }

    [TestCase("servicebus")]
    [TestCase("storage")]
    public async Task Publisher_OversizedText_RejectsBeforeCreatingClients(string kind)
    {
        await using var publisher = new QueuePublishers(new ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<ArgumentException>(() => kind == "servicebus"
            ? publisher.ServiceBusAsync(new string('t', 4097), false, default)
            : publisher.StorageQueueAsync(new string('t', 4097), default));
    }

    [Test]
    public async Task StorageQueueAsync_BoundaryText_CreatesQueueAndPublishesCompatibleJsonEnvelope()
    {
        var queue = new FakeQueueClient();
        string? name = null;
        QueueClientOptions? options = null;
        await using var publisher = CreatePublisher(Configuration(LOCAL_BUS), (_, _) => new FakeBusClient(), (connection, queueName, value) => { name = queueName; options = value; return queue; });
        using var cancellation = new CancellationTokenSource();
        var text = new string('t', 4096);
        var work = await publisher.StorageQueueAsync(text, cancellation.Token);
        Assert.That(name, Is.EqualTo("toolbox-function-work"));
        Assert.That(options!.MessageEncoding, Is.EqualTo(QueueMessageEncoding.Base64));
        Assert.That(queue.Operations, Is.EqualTo(new[] { "create", "send" }));
        Assert.That(queue.Tokens, Is.All.EqualTo(cancellation.Token));
        Assert.That(JsonSerializer.Deserialize<QueueWorkItem>(queue.Body!), Is.EqualTo(work));
        Assert.That(work.Text, Is.EqualTo(text));
    }

    [TestCase("https://remote.example/account")]
    [TestCase("http://local-looking-azurite.example/account")]
    public async Task StorageQueueAsync_RemoteEndpoint_RejectsBeforeFactoryUse(string endpoint)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ToolboxAzureStorage"] = $"DefaultEndpointsProtocol=http;AccountName=sample;AccountKey=c2FtcGxl;QueueEndpoint={endpoint};"
        }).Build();
        await using var publisher = CreatePublisher(configuration, (_, _) => new FakeBusClient(), (_, _, _) => throw new AssertionException("Client factory must not run."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.StorageQueueAsync("payload", default));
    }

    [Test]
    public async Task StorageQueueAsync_ComposeHost_AcceptsAzuriteEndpoint()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ToolboxAzureStorage"] = "DefaultEndpointsProtocol=http;AccountName=sample;AccountKey=c2FtcGxl;QueueEndpoint=http://azurite:10001/sample;"
        }).Build();
        var queue = new FakeQueueClient();
        await using var publisher = CreatePublisher(configuration, (_, _) => new FakeBusClient(), (_, _, _) => queue);
        await publisher.StorageQueueAsync("payload", default);
        Assert.That(queue.Operations, Is.EqualTo(new[] { "create", "send" }));
    }

    [TestCase("servicebus")]
    [TestCase("storage")]
    public async Task Publisher_SdkFailure_PropagatesAndDisposesSender(string kind)
    {
        var error = new IOException("emulator unavailable");
        var client = new FakeBusClient();
        client.Sender.Failure = error;
        var queue = new FakeQueueClient { Failure = error };
        await using var publisher = CreatePublisher(Configuration(LOCAL_BUS), (_, _) => client, (_, _, _) => queue);
        var thrown = await Assert.ThrowsAsync<IOException>(() => kind == "servicebus" ? publisher.ServiceBusAsync("work", false, default) : publisher.StorageQueueAsync("work", default));
        Assert.That(thrown, Is.SameAs(error));
        if (kind == "servicebus")
        {
            Assert.That(client.Sender.Disposed, Is.True);
        }
        else
        {
            Assert.That(queue.Operations, Is.EqualTo(new[] { "create" }));
        }
    }

    [TestCase("servicebus")]
    [TestCase("storage")]
    public async Task Publisher_CanceledInvocation_ForwardsTokenToSdk(string kind)
    {
        var client = new FakeBusClient();
        var queue = new FakeQueueClient();
        await using var publisher = CreatePublisher(Configuration(LOCAL_BUS), (_, _) => client, (_, _, _) => queue);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.CatchAsync<OperationCanceledException>(() => kind == "servicebus" ? publisher.ServiceBusAsync("work", false, cancellation.Token) : publisher.StorageQueueAsync("work", cancellation.Token));
        if (kind == "storage")
        {
            Assert.That(queue.Tokens, Has.Exactly(1).Items);
        }
        Assert.That(kind == "servicebus" ? client.Sender.Token : queue.Tokens.Single(), Is.EqualTo(cancellation.Token));
    }

    [Test]
    public async Task DisposeAsync_UnusedPublisher_DoesNotCreateAnSdkClient()
    {
        var publisher = CreatePublisher(new ConfigurationBuilder().Build(), (_, _) => throw new AssertionException("Disposal must not create a client."));
        await publisher.DisposeAsync();
    }

    private static IConfiguration Configuration(string? connection) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceBusConnection"] = connection }).Build();

    private static QueuePublishers CreatePublisher(IConfiguration configuration, Func<string, ServiceBusClientOptions, ServiceBusClient> busFactory,
        Func<string, string, QueueClientOptions, QueueClient>? queueFactory = null)
    {
        return new QueuePublishers(configuration, busFactory, queueFactory ?? ((_, _, _) => new FakeQueueClient()));
    }

    private sealed class FakeBusClient : ServiceBusClient
    {
        public FakeSender Sender { get; } = new();
        public string? Entity { get; private set; }
        public bool Disposed { get; private set; }
        public override ServiceBusSender CreateSender(string queueOrTopicName) { Entity = queueOrTopicName; return Sender; }
        public override ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class FakeSender : ServiceBusSender
    {
        public ServiceBusMessage? Message { get; private set; }
        public CancellationToken Token { get; private set; }
        public bool Disposed { get; private set; }
        public Exception? Failure { get; set; }
        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        {
            Message = message;
            Token = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
        public override ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class FakeQueueClient : QueueClient
    {
        public List<string> Operations { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public string? Body { get; private set; }
        public Exception? Failure { get; set; }
        public override Task<Response> CreateIfNotExistsAsync(IDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
        {
            Operations.Add("create");
            Tokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Failure is null ? Task.FromResult<Response>(new StubResponse()) : Task.FromException<Response>(Failure);
        }
        public override Task<Response<SendReceipt>> SendMessageAsync(string messageText, CancellationToken cancellationToken)
        {
            Operations.Add("send");
            Tokens.Add(cancellationToken);
            Body = messageText;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Response.FromValue<SendReceipt>(null!, new StubResponse()));
        }
    }

    private sealed class StubResponse : Response
    {
        public override int Status => 201;
        public override string ReasonPhrase => "Created";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = "test-request";
        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
        protected override bool TryGetHeader(string name, out string value) { value = ""; return false; }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values) { values = []; return false; }
    }
}
