namespace DeveloperToolbox.Example.Messaging;

/// <summary>Local messaging demonstrations available to the app and Functions examples.</summary>
public interface IMessagingSamples
{
    /// <summary>Publishes and acknowledges a RabbitMQ message.</summary>
    Task<string> RabbitMqAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends and receives a correlated SQS message.</summary>
    Task<string> SqsAsync(CancellationToken cancellationToken = default);

    /// <summary>Publishes an SNS notification and receives its SQS delivery.</summary>
    Task<string> SnsAsync(CancellationToken cancellationToken = default);

    /// <summary>Publishes an EventBridge event and receives its SQS envelope.</summary>
    Task<string> EventBridgeAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends JSON to the Service Bus demonstration queue.</summary>
    Task<string> ServiceBusAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends JSON to the Service Bus demonstration topic.</summary>
    Task<string> ServiceBusTopicAsync(CancellationToken cancellationToken = default);
}
