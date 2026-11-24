using System.Text;
using System.Text.Json;
using System.Net;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using Azure.Messaging.ServiceBus;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DeveloperToolbox.Example.Messaging.Tests")]

namespace DeveloperToolbox.Example.Messaging;

/// <summary>Small local-only broker and AWS messaging examples.</summary>
public sealed class MessagingSamples : IMessagingSamples, IDisposable
{
    private readonly IConfiguration configuration;
    private readonly Lazy<IAmazonSQS> sqs;
    private readonly Lazy<IAmazonSimpleNotificationService> sns;
    private readonly Lazy<IAmazonEventBridge> events;
    private readonly bool ownsClients = true;

    /// <summary>Creates reusable local-emulator clients only when an example needs them.</summary>
    public MessagingSamples(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        this.configuration = configuration;
        sqs = new(() => new AmazonSQSClient(
        new BasicAWSCredentials("test", "test"), new AmazonSQSConfig
        {
            ServiceURL = LocalEndpoint(configuration), AuthenticationRegion = "us-east-1"
        }));
        sns = new(() => new AmazonSimpleNotificationServiceClient(
        new BasicAWSCredentials("test", "test"), new AmazonSimpleNotificationServiceConfig
        {
            ServiceURL = LocalEndpoint(configuration), AuthenticationRegion = "us-east-1"
        }));
        events = new(() => new AmazonEventBridgeClient(
        new BasicAWSCredentials("test", "test"), new AmazonEventBridgeConfig
        {
            ServiceURL = LocalEndpoint(configuration), AuthenticationRegion = "us-east-1"
        }));
    }

    // Borrow injected SDK clients; their owner controls disposal independently of the sample.
    internal MessagingSamples(IConfiguration configuration, IAmazonSQS sqs,
        IAmazonSimpleNotificationService sns, IAmazonEventBridge events) : this(configuration)
    {
        ArgumentNullException.ThrowIfNull(sqs);
        ArgumentNullException.ThrowIfNull(sns);
        ArgumentNullException.ThrowIfNull(events);
        this.sqs = new(() => sqs);
        this.sns = new(() => sns);
        this.events = new(() => events);
        ownsClients = false;
    }

    /// <summary>Publishes a durable message and acknowledges it after reading it.</summary>
    public async Task<string> RabbitMqAsync(CancellationToken cancellationToken = default)
    {
        var host = configuration["RABBITMQ_HOST"] ?? "localhost";
        if (!string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(host, "rabbitmq", StringComparison.OrdinalIgnoreCase) &&
            !(IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address)))
        {
            throw new InvalidOperationException("RABBITMQ_HOST must identify the local toolbox broker.");
        }
        var configuredPort = configuration["RABBITMQ_PORT"] ?? "10190";
        if (!int.TryParse(configuredPort, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("RABBITMQ_PORT must be between 1 and 65535.");
        }
        var password = configuration["RABBITMQ_DEFAULT_PASS"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Set RABBITMQ_DEFAULT_PASS for the local broker.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var factory = new ConnectionFactory
        {
            HostName = host, Port = port,
            UserName = configuration["RABBITMQ_DEFAULT_USER"] ?? "toolbox",
            Password = password
        };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        // A unique queue isolates concurrent examples and is removed after the demo.
        var queue = $"toolbox-sample-{Guid.NewGuid():N}";
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        Exception? operationFailure = null;
        try
        {
            await channel.BasicPublishAsync("", queue, mandatory: true,
                new BasicProperties { Persistent = true, ContentType = "application/json" },
                Encoding.UTF8.GetBytes("{\"message\":\"Hello RabbitMQ\"}"), cancellationToken);
            var message = await channel.BasicGetAsync(queue, autoAck: false, cancellationToken);
            if (message is null)
            {
                throw new InvalidOperationException("The published message was not available.");
            }
            var body = Encoding.UTF8.GetString(message.Body.Span);
            await channel.BasicAckAsync(message.DeliveryTag, multiple: false, cancellationToken);
            return body;
        }
        catch (Exception exception)
        {
            operationFailure = exception;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await channel.QueueDeleteAsync(queue, cancellationToken: cleanup.Token);
            }
            catch (Exception cleanupFailure)
            {
                if (operationFailure is not null)
                {
                    throw new AggregateException("RabbitMQ validation failed and its temporary queue could not be removed.",
                        operationFailure, cleanupFailure);
                }
                throw;
            }
        }
    }

    /// <summary>Sends a work item and receives only that item's correlated response.</summary>
    public async Task<string> SqsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LocalEndpoint(configuration);
        var body = JsonSerializer.Serialize(new { id = Guid.NewGuid(), message = "Hello SQS" });
        var queue = await sqs.Value.GetQueueUrlAsync("toolbox-work", cancellationToken);
        await sqs.Value.SendMessageAsync(queue.QueueUrl, body, cancellationToken);
        return await ReceiveAsync("toolbox-work", body, cancellationToken);
    }

    /// <summary>Publishes to SNS and receives the fan-out message from its SQS subscription.</summary>
    public async Task<string> SnsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LocalEndpoint(configuration);
        var body = JsonSerializer.Serialize(new { id = Guid.NewGuid(), message = "Hello SNS" });
        var topic = await sns.Value.CreateTopicAsync("toolbox-notifications", cancellationToken);
        await sns.Value.PublishAsync(new PublishRequest { TopicArn = topic.TopicArn, Message = body }, cancellationToken);
        return await ReceiveAsync("toolbox-subscriber", body, cancellationToken);
    }

    /// <summary>Publishes an event and reads the EventBridge envelope from its SQS target.</summary>
    public async Task<string> EventBridgeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LocalEndpoint(configuration);
        var id = Guid.NewGuid().ToString();
        var response = await events.Value.PutEventsAsync(new PutEventsRequest
        {
            Entries = [new PutEventsRequestEntry
            {
                EventBusName = "toolbox", Source = "developertoolbox", DetailType = "ToolboxSample",
                Detail = JsonSerializer.Serialize(new { id, message = "Hello EventBridge" })
            }]
        }, cancellationToken);
        if (response.FailedEntryCount > 0)
        {
            throw new InvalidOperationException("EventBridge rejected the sample event.");
        }
        return await ReceiveAsync("toolbox-events", id, cancellationToken);
    }

    /// <summary>Sends JSON to the emulator's general demonstration queue.</summary>
    public Task<string> ServiceBusAsync(CancellationToken cancellationToken = default)
    {
        return SendServiceBusAsync("queue.1", cancellationToken);
    }

    /// <summary>Sends JSON to the emulator's general demonstration topic.</summary>
    public Task<string> ServiceBusTopicAsync(CancellationToken cancellationToken = default)
    {
        return SendServiceBusAsync("topic.1", cancellationToken);
    }

    private async Task<string> SendServiceBusAsync(string entity, CancellationToken cancellationToken)
    {
        var connection = LocalServiceBusConnection.Read(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        await using var client = new ServiceBusClient(connection.ConnectionString, new ServiceBusClientOptions
        {
            // The emulator otherwise uses AMQP port 5672 regardless of the Endpoint port.
            CustomEndpointAddress = connection.Endpoint
        });
        await using var sender = client.CreateSender(entity);
        var id = Guid.NewGuid().ToString("N");
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(new { id, text = "Hello Service Bus" }))
        {
            MessageId = id, ContentType = "application/json"
        };
        await sender.SendMessageAsync(message, cancellationToken);
        return $"Sent {message.MessageId} to {entity}.";
    }

    // Round-trip queues are for toolbox examples; receiving increments their shared redrive count.
    private async Task<string> ReceiveAsync(string queueName, string expected, CancellationToken cancellationToken)
    {
        return await ReceiveAsync(sqs.Value, queueName, expected, cancellationToken);
    }

    // Keep unmatched work hidden throughout polling, then restore it once without deleting it.
    internal static async Task<string> ReceiveAsync(IAmazonSQS client, string queueName, string expected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expected);
        cancellationToken.ThrowIfCancellationRequested();
        var queue = await client.GetQueueUrlAsync(queueName, cancellationToken);
        var deferredReceipts = new HashSet<string>(StringComparer.Ordinal);
        Exception? operationFailure = null;
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var response = await client.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queue.QueueUrl, WaitTimeSeconds = 2, MaxNumberOfMessages = 10,
                    VisibilityTimeout = 60
                }, cancellationToken);
                string? matchedBody = null;
                string? matchedReceipt = null;
                foreach (var message in response.Messages ?? [])
                {
                    if (matchedReceipt is null && IsCorrelated(message.Body, expected))
                    {
                        matchedBody = message.Body;
                        matchedReceipt = message.ReceiptHandle;
                    }
                    else
                    {
                        deferredReceipts.Add(message.ReceiptHandle);
                    }
                }
                if (matchedReceipt is not null)
                {
                    await client.DeleteMessageAsync(queue.QueueUrl, matchedReceipt, cancellationToken);
                    return matchedBody!;
                }
            }
            throw new TimeoutException($"No correlated message arrived in {queueName}.");
        }
        catch (Exception exception)
        {
            operationFailure = exception;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await Task.WhenAll(deferredReceipts.Select(receipt =>
                    client.ChangeMessageVisibilityAsync(queue.QueueUrl, receipt, 0, cleanup.Token)));
            }
            catch (Exception restorationFailure)
            {
                if (operationFailure is not null)
                {
                    throw new AggregateException("Polling failed and unmatched messages could not all be restored.",
                        operationFailure, restorationFailure);
                }
                throw;
            }
        }
    }

    private static bool IsCorrelated(string body, string expected)
    {
        if (string.Equals(body, expected, StringComparison.Ordinal))
        {
            return true;
        }
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.Object
                && detail.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && string.Equals(id.GetString(), expected, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string LocalEndpoint(IConfiguration configuration)
    {
        var endpoint = configuration["TOOLBOX_AWS_ENDPOINT"] ?? "http://localhost:10195";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (!uri.IsLoopback && !string.Equals(uri.Host, "aws-emulator", StringComparison.OrdinalIgnoreCase)) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("TOOLBOX_AWS_ENDPOINT must identify a local emulator.");
        }
        return endpoint;
    }

    /// <summary>Releases the reusable SDK clients that were used by this process.</summary>
    public void Dispose()
    {
        if (!ownsClients) { return; }
        if (sqs.IsValueCreated) { sqs.Value.Dispose(); }
        if (sns.IsValueCreated) { sns.Value.Dispose(); }
        if (events.IsValueCreated) { events.Value.Dispose(); }
    }
}
