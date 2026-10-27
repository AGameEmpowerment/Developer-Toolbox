using System.Net;
using DeveloperToolbox.Example.Data;
using DeveloperToolbox.Example.Messaging;
using DeveloperToolbox.Example.Resources;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DeveloperToolbox.Example.Functions;

/// <summary>Runs the same SDK examples from local Functions HTTP triggers.</summary>
public sealed class ResourceFunctions(IResourceSamples resources, IMessagingSamples messaging, IDatabaseSamples databases, IQueuePublishers publishers)
{
    /// <summary>POST /api/samples/{resource} runs one sample and returns its result.</summary>
    [Function("ToolboxResourceSample")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "samples/{resource}")] HttpRequestData request,
        string resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        object? result = resource.ToLowerInvariant() switch
        {
            "sqlserver" => await databases.SqlServerAsync(cancellationToken: cancellationToken),
            "postgres" => await databases.PostgresAsync(cancellationToken: cancellationToken),
            "cosmos" => await resources.CosmosAsync(cancellationToken),
            "dynamodb" => await resources.DynamoDbAsync(cancellationToken),
            "redis" => await resources.RedisAsync(cancellationToken),
            "mail" => await resources.MailAsync(cancellationToken),
            "seq" => await resources.SeqAsync(cancellationToken),
            "wiremock" => await resources.WireMockAsync(cancellationToken),
            "blob" => await resources.BlobAsync(cancellationToken),
            "storage-queue" => await resources.StorageQueueAsync(cancellationToken),
            "table" => await resources.TableAsync(cancellationToken),
            "s3" => await resources.S3Async(cancellationToken),
            "rabbitmq" => await messaging.RabbitMqAsync(cancellationToken),
            "sqs" => await messaging.SqsAsync(cancellationToken),
            "sns" => await messaging.SnsAsync(cancellationToken),
            "eventbridge" => await messaging.EventBridgeAsync(cancellationToken),
            "servicebus-queue" => await publishers.ServiceBusAsync("Hello Service Bus queue", false, cancellationToken),
            "servicebus-topic" => await publishers.ServiceBusAsync("Hello Service Bus topic", true, cancellationToken),
            "function-storage-queue" => await publishers.StorageQueueAsync("Hello Azurite function queue", cancellationToken),
            _ => null
        };
        var response = request.CreateResponse(result is null ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result is null ? new { error = "Unknown toolbox resource." } : result, cancellationToken);
        return response;
    }
}
