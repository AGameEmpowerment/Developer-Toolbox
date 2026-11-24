using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues.Models;
using DeveloperToolbox.Example.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DeveloperToolbox.Example.Functions;

/// <summary>Native Azure queue triggers acknowledge only after the EF Core write succeeds.</summary>
public sealed class QueueFunctions(IDatabaseSamples databases, ILogger<QueueFunctions> logger)
{
    /// <summary>Persists Service Bus queue work in SQL Server.</summary>
    [Function("ServiceBusQueueToSqlServer")]
    public async Task ServiceBusQueueAsync(
        [ServiceBusTrigger("queue.functions", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var work = Deserialize(message.Body);
        var inserted = await databases.SaveSqlServerMessageAsync(work.Id, work.Text, "servicebus-queue", cancellationToken);
        logger.LogInformation("Service Bus queue message {MessageId}: inserted={Inserted}", work.Id, inserted);
    }

    /// <summary>Persists the existing topic subscription's work in PostgreSQL.</summary>
    [Function("ServiceBusTopicToPostgres")]
    public async Task ServiceBusTopicAsync(
        [ServiceBusTrigger("topic.functions", "subscription.functions", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var work = Deserialize(message.Body);
        var inserted = await databases.SavePostgresMessageAsync(work.Id, work.Text, "servicebus-topic", cancellationToken);
        logger.LogInformation("Service Bus topic message {MessageId}: inserted={Inserted}", work.Id, inserted);
    }

    /// <summary>Persists Azurite queue work in PostgreSQL; poison handling is provided by the queue binding.</summary>
    [Function("StorageQueueToPostgres")]
    public async Task StorageQueueAsync(
        [QueueTrigger("toolbox-function-work", Connection = "ToolboxAzureStorage")] QueueMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var work = Deserialize(message.Body);
        var inserted = await databases.SavePostgresMessageAsync(work.Id, work.Text, "azurite-queue", cancellationToken);
        logger.LogInformation("Storage queue message {MessageId}: inserted={Inserted}", work.Id, inserted);
    }

    private static QueueWorkItem Deserialize(BinaryData body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var work = body.ToObjectFromJson<QueueWorkItem>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new ArgumentException("A queue work item is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(work.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(work.Text);
        if (work.Id.Length > 128 || work.Text.Length > 4096)
        {
            throw new ArgumentException("Queue work requires an ID up to 128 and text up to 4096 characters.");
        }
        return work;
    }
}
