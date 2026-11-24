using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using DeveloperToolbox.Example.Resources;
using NUnit.Framework;

namespace DeveloperToolbox.Example.Resources.Tests;

public sealed class AzureQueueAndTableTests
{
    [Test]
    public async Task StorageQueueAsync_RoundTrip_UsesIsolatedQueueAndDeletesReceivedMessageBeforeQueue()
    {
        var queue = new FakeQueue();
        using var samples = Create(queue);

        Assert.That(await samples.StorageQueueAsync(), Is.EqualTo("Hello Azure Storage Queue"));

        Assert.That(queue.RequestedName, Does.StartWith("toolbox-sample-"));
        Assert.That(queue.Operations, Is.EqualTo(new[] { "create", "send", "receive", "delete-message", "delete-queue" }));
        Assert.That(queue.Sent, Is.EqualTo("Hello Azure Storage Queue"));
        Assert.That(queue.DeletedMessage, Is.EqualTo(("message-id", "pop-receipt")));
        Assert.That(queue.CleanupToken.CanBeCanceled, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StorageQueueAsync_InvalidRead_RemovesQueueWithoutAcknowledgingMessage(bool missing)
    {
        var queue = new FakeQueue { Content = "wrong content", MissingMessage = missing };
        using var samples = Create(queue);

        await Assert.ThrowsAsync<InvalidOperationException>(() => samples.StorageQueueAsync());

        Assert.That(queue.DeletedMessage, Is.Null);
        Assert.That(queue.Operations.Last(), Is.EqualTo("delete-queue"));
    }

    [Test]
    public async Task StorageQueueAsync_CanceledRead_DeletesQueueWithIndependentToken()
    {
        using var caller = new CancellationTokenSource();
        var queue = new FakeQueue { BeforeRead = caller.Cancel };
        using var samples = Create(queue);

        await Assert.CatchAsync<OperationCanceledException>(() => samples.StorageQueueAsync(caller.Token));

        Assert.That(queue.CleanupToken, Is.Not.EqualTo(caller.Token));
        Assert.That(queue.CleanupToken.IsCancellationRequested, Is.False);
        Assert.That(queue.Operations.Last(), Is.EqualTo("delete-queue"));
    }

    [Test]
    public async Task TableAsync_RoundTrip_UsesStableKeysAndRemovesOnlyCreatedEntity()
    {
        var table = new FakeTable();
        using var samples = Create(table);

        Assert.That(await samples.TableAsync(), Is.EqualTo("Hello Azure Table Storage"));

        Assert.That(table.RequestedName, Is.EqualTo("ToolboxSamples"));
        Assert.That(table.Written!.PartitionKey, Is.EqualTo("samples"));
        Assert.That(Guid.TryParseExact(table.Written.RowKey, "N", out _), Is.True);
        Assert.That(table.Written.GetString("Message"), Is.EqualTo("Hello Azure Table Storage"));
        Assert.That(table.ReadKeys, Is.EqualTo((table.Written.PartitionKey, table.Written.RowKey)));
        Assert.That(table.DeletedKeys, Is.EqualTo(table.ReadKeys));
        Assert.That(table.Operations, Is.EqualTo(new[] { "create", "add", "get", "delete" }));
        Assert.That(table.CleanupToken.CanBeCanceled, Is.True);
    }

    [Test]
    public async Task TableAsync_MismatchedRead_RemovesEntityAndRejectsWrongValue()
    {
        var table = new FakeTable { Content = "wrong content" };
        using var samples = Create(table);

        await Assert.ThrowsAsync<InvalidOperationException>(() => samples.TableAsync());

        Assert.That(table.DeletedKeys, Is.EqualTo(table.ReadKeys));
        Assert.That(table.Operations.Last(), Is.EqualTo("delete"));
    }

    [Test]
    public async Task TableAsync_CanceledRead_RemovesEntityWithIndependentToken()
    {
        using var caller = new CancellationTokenSource();
        var table = new FakeTable { BeforeRead = caller.Cancel };
        using var samples = Create(table);

        await Assert.CatchAsync<OperationCanceledException>(() => samples.TableAsync(caller.Token));

        Assert.That(table.CleanupToken, Is.Not.EqualTo(caller.Token));
        Assert.That(table.CleanupToken.IsCancellationRequested, Is.False);
        Assert.That(table.Operations.Last(), Is.EqualTo("delete"));
    }

    private static ResourceSamples Create(FakeQueue queue) => new(ResourceSamplesTests.Configuration(),
        new ResourceSamplesTests.RejectHttpFactory(), null, null, queueService: new FakeQueueService(queue));

    private static ResourceSamples Create(FakeTable table) => new(ResourceSamplesTests.Configuration(),
        new ResourceSamplesTests.RejectHttpFactory(), null, null, tableService: new FakeTableService(table));

    private sealed class FakeQueueService(FakeQueue queue) : QueueServiceClient(new Uri("http://localhost:10001/devstoreaccount1"))
    {
        public override QueueClient GetQueueClient(string queueName)
        {
            queue.RequestedName = queueName;
            return queue;
        }
    }

    private sealed class FakeQueue : QueueClient
    {
        public string RequestedName { get; set; } = "";
        public string? Sent { get; private set; }
        public string Content { get; init; } = "Hello Azure Storage Queue";
        public bool MissingMessage { get; init; }
        public Action? BeforeRead { get; init; }
        public List<string> Operations { get; } = [];
        public (string Id, string Receipt)? DeletedMessage { get; private set; }
        public CancellationToken CleanupToken { get; private set; }

        public override Task<Response> CreateAsync(IDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
        {
            Operations.Add("create");
            return Task.FromResult<Response>(null!);
        }

        public override Task<Response<SendReceipt>> SendMessageAsync(string messageText, CancellationToken cancellationToken)
        {
            Operations.Add("send");
            Sent = messageText;
            return Task.FromResult(Response.FromValue<SendReceipt>(null!, null!));
        }

        public override Task<Response<QueueMessage>> ReceiveMessageAsync(TimeSpan? visibilityTimeout = null, CancellationToken cancellationToken = default)
        {
            Operations.Add("receive");
            BeforeRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            var message = MissingMessage ? null : QueuesModelFactory.QueueMessage(
                "message-id", "pop-receipt", BinaryData.FromString(Content), 1, null, null, null);
            return Task.FromResult(Response.FromValue(message!, null!));
        }

        public override Task<Response> DeleteMessageAsync(string messageId, string popReceipt, CancellationToken cancellationToken = default)
        {
            Operations.Add("delete-message");
            DeletedMessage = (messageId, popReceipt);
            return Task.FromResult<Response>(null!);
        }

        public override Task<Response<bool>> DeleteIfExistsAsync(CancellationToken cancellationToken = default)
        {
            Operations.Add("delete-queue");
            CleanupToken = cancellationToken;
            return Task.FromResult(Response.FromValue(true, null!));
        }
    }

    private sealed class FakeTableService(FakeTable table) : TableServiceClient("UseDevelopmentStorage=true")
    {
        public override TableClient GetTableClient(string tableName)
        {
            table.RequestedName = tableName;
            return table;
        }
    }

    private sealed class FakeTable : TableClient
    {
        public string RequestedName { get; set; } = "";
        public TableEntity? Written { get; private set; }
        public string Content { get; init; } = "Hello Azure Table Storage";
        public Action? BeforeRead { get; init; }
        public List<string> Operations { get; } = [];
        public (string Partition, string Row)? ReadKeys { get; private set; }
        public (string Partition, string Row)? DeletedKeys { get; private set; }
        public CancellationToken CleanupToken { get; private set; }

        public override Task<Response<TableItem>> CreateIfNotExistsAsync(CancellationToken cancellationToken = default)
        {
            Operations.Add("create");
            return Task.FromResult(Response.FromValue<TableItem>(null!, null!));
        }

        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            Operations.Add("add");
            Assert.That(entity, Is.TypeOf<TableEntity>());
            Written = (TableEntity)(object)entity;
            return Task.FromResult<Response>(null!);
        }

        public override Task<Response<T>> GetEntityAsync<T>(string partitionKey, string rowKey, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            Operations.Add("get");
            ReadKeys = (partitionKey, rowKey);
            BeforeRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Response.FromValue((T)(ITableEntity)new TableEntity(partitionKey, rowKey) { ["Message"] = Content }, null!));
        }

        public override Task<Response> DeleteEntityAsync(string partitionKey, string rowKey, ETag ifMatch = default, CancellationToken cancellationToken = default)
        {
            Operations.Add("delete");
            DeletedKeys = (partitionKey, rowKey);
            CleanupToken = cancellationToken;
            return Task.FromResult<Response>(null!);
        }
    }
}
