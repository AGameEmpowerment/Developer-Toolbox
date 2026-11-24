using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;

namespace DeveloperToolbox.Example.Messaging;

/// <summary>Live local Service Bus validators using dedicated queue and subscription entities.</summary>
public sealed class ServiceBusValidationSamples
{
    private readonly IConfiguration configuration;
    private readonly Func<string, ServiceBusClientOptions, ServiceBusClient> createClient;

    /// <summary>Uses the Service Bus emulator SDK to validate running toolbox containers.</summary>
    public ServiceBusValidationSamples(IConfiguration configuration)
        : this(configuration, (connection, options) => new ServiceBusClient(connection, options))
    {
    }

    // SDK factories keep validator behavior testable without connecting to a broker.
    internal ServiceBusValidationSamples(IConfiguration configuration,
        Func<string, ServiceBusClientOptions, ServiceBusClient> createClient)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(createClient);
        this.configuration = configuration;
        this.createClient = createClient;
    }

    /// <summary>Sends and completes a uniquely correlated message on queue.validation.</summary>
    public Task<string> ValidateQueueAsync(CancellationToken cancellationToken = default) =>
        ValidateAsync("queue.validation", null, cancellationToken);

    /// <summary>Validates delivery through topic.validation and subscription.validation.</summary>
    public Task<string> ValidateTopicAsync(CancellationToken cancellationToken = default) =>
        ValidateAsync("topic.validation", "subscription.validation", cancellationToken);

    // Keep unrelated messages locked until polling ends to avoid repeatedly receiving the same work.
    private async Task<string> ValidateAsync(string entity, string? subscription, CancellationToken cancellationToken)
    {
        var connection = LocalServiceBusConnection.Read(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        await using var client = createClient(connection.ConnectionString, new ServiceBusClientOptions
        {
            CustomEndpointAddress = connection.Endpoint,
            RetryOptions = new ServiceBusRetryOptions { MaxRetries = 0, TryTimeout = TimeSpan.FromSeconds(10) }
        });
        await using var sender = client.CreateSender(entity);
        var receiverOptions = new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock };
        var receiver = subscription is null
            ? client.CreateReceiver(entity, receiverOptions)
            : client.CreateReceiver(entity, subscription, receiverOptions);
        var messageId = Guid.NewGuid().ToString("N");
        var unmatched = new Dictionary<string, ServiceBusReceivedMessage>(StringComparer.Ordinal);
        Exception? operationFailure = null;
        try
        {
            await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(new { message = "Toolbox Service Bus validation" }))
            {
                MessageId = messageId, ContentType = "application/json"
            }, cancellationToken);
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var batch = await receiver.ReceiveMessagesAsync(10, TimeSpan.FromSeconds(2), cancellationToken);
                ServiceBusReceivedMessage? correlated = null;
                foreach (var message in batch)
                {
                    if (correlated is null && string.Equals(message.MessageId, messageId, StringComparison.Ordinal))
                    {
                        correlated = message;
                    }
                    else
                    {
                        unmatched[message.LockToken] = message;
                    }
                }
                if (correlated is not null)
                {
                    await receiver.CompleteMessageAsync(correlated, cancellationToken);
                    return subscription is null ? "Validated Service Bus queue delivery." : "Validated Service Bus topic delivery.";
                }
            }
            throw new TimeoutException("The correlated Service Bus validation message did not arrive.");
        }
        catch (Exception exception)
        {
            operationFailure = exception;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Exception? cleanupFailure = null;
            try
            {
                await Task.WhenAll(unmatched.Values.Select(message =>
                    receiver.AbandonMessageAsync(message, cancellationToken: cleanup.Token)));
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
            // CloseAsync is the SDK's disposal operation and accepts our remaining cleanup budget.
            try
            {
                await receiver.CloseAsync(cleanup.Token);
            }
            catch (Exception exception)
            {
                cleanupFailure = cleanupFailure is null ? exception : new AggregateException(cleanupFailure, exception);
            }
            if (cleanupFailure is not null)
            {
                if (operationFailure is not null)
                {
                    throw new AggregateException("Service Bus validation failed and receiver cleanup could not finish.",
                        operationFailure, cleanupFailure);
                }
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
        }
    }
}
