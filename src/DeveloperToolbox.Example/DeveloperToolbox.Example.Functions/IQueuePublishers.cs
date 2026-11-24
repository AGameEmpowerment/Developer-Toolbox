namespace DeveloperToolbox.Example.Functions;

/// <summary>Local native queue producers available to the HTTP examples.</summary>
public interface IQueuePublishers
{
    /// <summary>Sends a bounded work item to the Service Bus queue or topic.</summary>
    Task<QueueWorkItem> ServiceBusAsync(string text, bool topic, CancellationToken cancellationToken);

    /// <summary>Sends a bounded work item to the native Azure Storage queue.</summary>
    Task<QueueWorkItem> StorageQueueAsync(string text, CancellationToken cancellationToken);
}
