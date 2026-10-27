using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;
using DeveloperToolbox.Example.Messaging;
using Microsoft.Extensions.Configuration;

namespace DeveloperToolbox.Example.Functions;

/// <summary>A bounded envelope shared by the native Azure queue producers and consumers.</summary>
public sealed record QueueWorkItem(string Id, string Text);

/// <summary>Publishes local work for the Functions queue triggers.</summary>
public sealed class QueuePublishers : IQueuePublishers, IAsyncDisposable
{
    private readonly IConfiguration configuration;
    private readonly Lazy<ServiceBusClient> serviceBus;
    private readonly Func<string, string, QueueClientOptions, QueueClient> queueFactory;

    /// <summary>Creates reusable local Azure SDK clients on first use.</summary>
    public QueuePublishers(IConfiguration configuration)
        : this(configuration, (connection, options) => new ServiceBusClient(connection, options),
            (connection, name, options) => new QueueClient(connection, name, options))
    {
    }

    // The SDK factory seam keeps tests in memory while retaining configuration guards before factory calls.
    internal QueuePublishers(IConfiguration configuration,
        Func<string, ServiceBusClientOptions, ServiceBusClient> serviceBusFactory,
        Func<string, string, QueueClientOptions, QueueClient> queueFactory)
    {
        this.configuration = configuration;
        this.queueFactory = queueFactory;
        serviceBus = new Lazy<ServiceBusClient>(() => CreateServiceBusClient(serviceBusFactory));
    }

    private ServiceBusClient CreateServiceBusClient(Func<string, ServiceBusClientOptions, ServiceBusClient> factory)
    {
        var connection = LocalServiceBusConnection.Read(configuration);
        return factory(connection.ConnectionString, new ServiceBusClientOptions { CustomEndpointAddress = connection.Endpoint });
    }

    /// <summary>Sends to the dedicated Functions queue for SQL Server, or topic for PostgreSQL.</summary>
    public async Task<QueueWorkItem> ServiceBusAsync(string text, bool topic, CancellationToken cancellationToken)
    {
        var work = CreateWork(text);
        await using var sender = serviceBus.Value.CreateSender(topic ? "topic.functions" : "queue.functions");
        await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(work))
        {
            MessageId = work.Id,
            ContentType = "application/json"
        }, cancellationToken);
        return work;
    }

    /// <summary>Sends a base64 envelope for the native Azurite queue trigger.</summary>
    public async Task<QueueWorkItem> StorageQueueAsync(string text, CancellationToken cancellationToken)
    {
        var work = CreateWork(text);
        var connection = configuration["ToolboxAzureStorage"] ?? "UseDevelopmentStorage=true";
        // Current Azurite supports this version; newer SDK defaults can outpace the emulator.
        var options = new QueueClientOptions(QueueClientOptions.ServiceVersion.V2026_06_06)
        {
            MessageEncoding = QueueMessageEncoding.Base64
        };
        var endpoint = new QueueClient(connection, "toolbox-function-work", options).Uri;
        if (!endpoint.IsLoopback && endpoint.Host != "azurite")
        {
            throw new InvalidOperationException("This queue producer requires local Azurite.");
        }
        var queue = queueFactory(connection, "toolbox-function-work", options);
        await queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await queue.SendMessageAsync(JsonSerializer.Serialize(work), cancellationToken);
        return work;
    }

    private static QueueWorkItem CreateWork(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > 4096)
        {
            throw new ArgumentException("Queue text must be at most 4096 characters.", nameof(text));
        }
        return new QueueWorkItem(Guid.NewGuid().ToString("N"), text);
    }

    /// <summary>Releases the reusable Service Bus client if this sample created it.</summary>
    public async ValueTask DisposeAsync()
    {
        if (serviceBus.IsValueCreated)
        {
            await serviceBus.Value.DisposeAsync();
        }
    }
}
