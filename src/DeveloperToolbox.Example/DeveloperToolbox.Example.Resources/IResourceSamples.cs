namespace DeveloperToolbox.Example.Resources;

/// <summary>Independent asynchronous demonstrations of the toolbox's local resource APIs.</summary>
public interface IResourceSamples
{
    /// <summary>Writes and reads a Cosmos document.</summary>
    Task<string> CosmosAsync(CancellationToken cancellationToken = default);
    /// <summary>Writes and reads a DynamoDB item.</summary>
    Task<string> DynamoDbAsync(CancellationToken cancellationToken = default);
    /// <summary>Writes and reads a temporary Redis key.</summary>
    Task<string> RedisAsync(CancellationToken cancellationToken = default);
    /// <summary>Sends synthetic mail to the local capture server.</summary>
    Task<string> MailAsync(CancellationToken cancellationToken = default);
    /// <summary>Emits a structured Seq event.</summary>
    Task<string> SeqAsync(CancellationToken cancellationToken = default);
    /// <summary>Creates, calls, and removes a temporary WireMock mapping.</summary>
    Task<string> WireMockAsync(CancellationToken cancellationToken = default);
    /// <summary>Writes, reads, and removes an Azure blob.</summary>
    Task<string> BlobAsync(CancellationToken cancellationToken = default);
    /// <summary>Sends and receives work in a temporary Azure queue.</summary>
    Task<string> StorageQueueAsync(CancellationToken cancellationToken = default);
    /// <summary>Writes, reads, and removes an Azure table entity.</summary>
    Task<string> TableAsync(CancellationToken cancellationToken = default);
    /// <summary>Writes, reads, and removes a local S3 object.</summary>
    Task<string> S3Async(CancellationToken cancellationToken = default);
}
