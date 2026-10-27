using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using MailKit.Security;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using MimeKit;
using StackExchange.Redis;

namespace DeveloperToolbox.Example.Resources;

/// <summary>Independent, local-development-only write/read examples for toolbox services.</summary>
/// <remarks>Register the named HTTP client with redirects and proxies disabled; optional clients initialize on first use.</remarks>
public sealed class ResourceSamples : IResourceSamples, IDisposable
{
    /// <summary>The local Seq/WireMock client registration, with automatic redirects and proxies disabled.</summary>
    public const string HttpClientName = "ToolboxResourceSamples";

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Lazy<CosmosClient> _cosmos;
    private readonly Lazy<IAmazonDynamoDB> _dynamoDb;
    private readonly Lazy<IAmazonS3> _s3;
    private readonly Lazy<Task<ConnectionMultiplexer>> _redis;
    private readonly Lazy<BlobServiceClient> _blobs;
    private readonly Lazy<QueueServiceClient> _queues;
    private readonly Lazy<TableServiceClient> _tables;

    /// <summary>Configures lazy clients without requiring unrelated services or credentials.</summary>
    public ResourceSamples(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        : this(configuration, httpClientFactory, null, null)
    {
    }

    // SDK interfaces isolate workflows while every factory still receives a validated local destination.
    internal ResourceSamples(IConfiguration configuration, IHttpClientFactory httpClientFactory,
        Func<AmazonDynamoDBConfig, IAmazonDynamoDB>? dynamoDbFactory,
        Func<AmazonS3Config, IAmazonS3>? s3Factory, BlobServiceClient? blobService = null,
        QueueServiceClient? queueService = null, TableServiceClient? tableService = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _cosmos = new(CreateCosmosClient);
        _dynamoDb = new(() => (dynamoDbFactory ?? (config => new AmazonDynamoDBClient(LocalCredentials(), config)))(new AmazonDynamoDBConfig
        {
            ServiceURL = LocalEndpoint("DynamoDb", "http://localhost:10180", "dynamodb").AbsoluteUri,
            AuthenticationRegion = "us-east-1",
            MaxErrorRetry = 1,
            Timeout = TimeSpan.FromSeconds(15)
        }));
        _s3 = new(() => (s3Factory ?? (config => new AmazonS3Client(LocalCredentials(), config)))(new AmazonS3Config
        {
            ServiceURL = LocalEndpoint("S3", _configuration["TOOLBOX_AWS_ENDPOINT"] ?? "http://localhost:10195", "aws-emulator").AbsoluteUri,
            AuthenticationRegion = "us-east-1",
            ForcePathStyle = true,
            MaxErrorRetry = 1,
            Timeout = TimeSpan.FromSeconds(15)
        }));
        _redis = new(CreateRedisAsync);
        // Azurite may lag the latest SDK default REST version; keep the sample's supported API explicit.
        _blobs = new(() => ValidateStorageClient(blobService ?? new BlobServiceClient(StorageConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2026_06_06))));
        _queues = new(() => ValidateStorageClient(queueService ?? new QueueServiceClient(StorageConnectionString(),
            new QueueClientOptions(QueueClientOptions.ServiceVersion.V2026_06_06))));
        _tables = new(() => ValidateStorageClient(tableService ?? new TableServiceClient(StorageConnectionString())));
    }

    /// <summary>Creates a sample container, writes/reads a partitioned document, then deletes the document.</summary>
    public async Task<string> CosmosAsync(CancellationToken cancellationToken = default)
    {
        var database = await _cosmos.Value.CreateDatabaseIfNotExistsAsync("toolbox-samples", cancellationToken: cancellationToken);
        var container = await database.Database.CreateContainerIfNotExistsAsync("documents", "/id", cancellationToken: cancellationToken);
        var id = Guid.NewGuid().ToString("N");
        var document = new { id, message = "Hello Cosmos DB" };
        await container.Container.CreateItemAsync(document, new PartitionKey(id), cancellationToken: cancellationToken);
        return await WithCleanupAsync(async () =>
        {
            var read = await container.Container.ReadItemAsync<Dictionary<string, JsonElement>>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            Verify(read.Resource["message"].GetString()!, document.message);
            return JsonSerializer.Serialize(read.Resource);
        }, cleanupToken => container.Container.DeleteItemAsync<object>(id, new PartitionKey(id), cancellationToken: cleanupToken));
    }

    /// <summary>Creates a simple key/value table in DynamoDB Local and verifies a consistent document read.</summary>
    public async Task<string> DynamoDbAsync(CancellationToken cancellationToken = default)
    {
        const string TABLE = "toolbox-samples";
        await EnsureDynamoTableAsync(TABLE, cancellationToken);
        var id = Guid.NewGuid().ToString("N");
        var key = new Dictionary<string, AttributeValue> { ["Id"] = new() { S = id } };
        await _dynamoDb.Value.PutItemAsync(new PutItemRequest
        {
            TableName = TABLE,
            Item = new() { ["Id"] = new() { S = id }, ["Message"] = new() { S = "Hello DynamoDB" } }
        }, cancellationToken);
        return await WithCleanupAsync(async () =>
        {
            var read = await _dynamoDb.Value.GetItemAsync(new GetItemRequest { TableName = TABLE, Key = key, ConsistentRead = true }, cancellationToken);
            Verify(read.Item["Message"].S, "Hello DynamoDB");
            return $"{id}: {read.Item["Message"].S}";
        }, cleanupToken => _dynamoDb.Value.DeleteItemAsync(TABLE, key, cleanupToken));
    }

    /// <summary>Stores a Redis string with expiration, reads it, then removes only that sample key.</summary>
    public async Task<string> RedisAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _redis.Value.WaitAsync(cancellationToken);
        var database = connection.GetDatabase();
        var key = $"toolbox:samples:{Guid.NewGuid():N}";
        await database.StringSetAsync(key, "Hello Redis", TimeSpan.FromMinutes(5)).WaitAsync(cancellationToken);
        return await WithCleanupAsync(async () =>
        {
            var value = (await database.StringGetAsync(key).WaitAsync(cancellationToken)).ToString();
            Verify(value, "Hello Redis");
            return value;
        }, cleanupToken => database.KeyDeleteAsync(key).WaitAsync(cleanupToken));
    }

    /// <summary>Sends synthetic mail to the local smtp4dev capture server for inspection in its web UI.</summary>
    public async Task<string> MailAsync(CancellationToken cancellationToken = default)
    {
        var host = _configuration["Resources:Mail:Host"] ?? "localhost";
        ValidateLocalUri(new UriBuilder("http", host).Uri, "smtp4dev");
        var portText = _configuration["Resources:Mail:Port"] ?? "10130";
        var port = int.Parse(portText, CultureInfo.InvariantCulture);
        var id = Guid.NewGuid().ToString("N");
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Toolbox Sample", "sender@example.test"));
        message.To.Add(new MailboxAddress("Toolbox Developer", "recipient@example.test"));
        message.Subject = $"Toolbox sample {id}";
        message.Body = new TextPart("plain") { Text = "Hello smtp4dev. This is synthetic local development mail." };
        using var smtp = new MailKit.Net.Smtp.SmtpClient { Timeout = 15000 };
        await smtp.ConnectAsync(host, port, SecureSocketOptions.None, cancellationToken);
        await smtp.SendAsync(message, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
        return $"Captured mail: {message.Subject}";
    }

    /// <summary>Emits a structured CLEF event that can be inspected by its SampleId in Seq.</summary>
    public async Task<string> SeqAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = LocalEndpoint("Seq", "http://localhost:10150", "otlp-seq");
        var id = Guid.NewGuid().ToString("N");
        var clef = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["@t"] = DateTimeOffset.UtcNow.ToString("O"), ["@mt"] = "Toolbox sample {SampleId}",
            ["SampleId"] = id, ["Application"] = "DeveloperToolbox.Example"
        });
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "api/events/raw"))
        {
            Content = new StringContent(clef + "\n", Encoding.UTF8, "application/vnd.serilog.clef")
        };
        var apiKey = _configuration["Resources:Seq:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Add("X-Seq-ApiKey", apiKey);
        }
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return $"Seq event SampleId = '{id}'";
    }

    /// <summary>Creates a temporary WireMock mapping, calls it, and removes only that mapping.</summary>
    public async Task<string> WireMockAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = LocalEndpoint("WireMock", "http://localhost:10080", "wiremock");
        var id = Guid.NewGuid();
        var path = $"/toolbox-samples/{id:N}";
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var created = await client.PostAsJsonAsync(new Uri(endpoint, "__admin/mappings"), new
        {
            id,
            request = new { method = "GET", urlPath = path },
            response = new { status = 200, body = "Hello WireMock", headers = new Dictionary<string, string> { ["Content-Type"] = "text/plain" } }
        }, cancellationToken);
        created.EnsureSuccessStatusCode();
        return await WithCleanupAsync(async () =>
        {
            var body = await client.GetStringAsync(new Uri(endpoint, path), cancellationToken);
            Verify(body, "Hello WireMock");
            return body;
        }, async cleanupToken =>
        {
            using var deleted = await client.DeleteAsync(new Uri(endpoint, $"__admin/mappings/{id}"), cleanupToken);
            deleted.EnsureSuccessStatusCode();
        });
    }

    /// <summary>Uploads/downloads an Azurite blob and removes the sample object.</summary>
    public async Task<string> BlobAsync(CancellationToken cancellationToken = default)
    {
        var container = _blobs.Value.GetBlobContainerClient("toolbox-samples");
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var blob = container.GetBlobClient($"sample-{Guid.NewGuid():N}.txt");
        await blob.UploadAsync(BinaryData.FromString("Hello Azure Blob Storage"), cancellationToken);
        return await WithCleanupAsync(async () =>
        {
            var read = await blob.DownloadContentAsync(cancellationToken);
            var text = read.Value.Content.ToString();
            Verify(text, "Hello Azure Blob Storage");
            return text;
        }, cleanupToken => blob.DeleteIfExistsAsync(cancellationToken: cleanupToken));
    }

    /// <summary>Sends, receives, and deletes an Azure Storage Queue message in an isolated temporary queue.</summary>
    public async Task<string> StorageQueueAsync(CancellationToken cancellationToken = default)
    {
        var queue = _queues.Value.GetQueueClient($"toolbox-sample-{Guid.NewGuid():N}");
        await queue.CreateAsync(cancellationToken: cancellationToken);
        return await WithCleanupAsync(async () =>
        {
            await queue.SendMessageAsync("Hello Azure Storage Queue", cancellationToken);
            var read = await queue.ReceiveMessageAsync(cancellationToken: cancellationToken);
            if (read.Value is null)
            {
                throw new InvalidOperationException("The sample queue message was not available.");
            }
            var text = read.Value.Body.ToString();
            Verify(text, "Hello Azure Storage Queue");
            await queue.DeleteMessageAsync(read.Value.MessageId, read.Value.PopReceipt, cancellationToken);
            return text;
        }, cleanupToken => queue.DeleteIfExistsAsync(cleanupToken));
    }

    /// <summary>Writes/reads an Azure Table entity with a partition and row key, then removes the entity.</summary>
    public async Task<string> TableAsync(CancellationToken cancellationToken = default)
    {
        var table = _tables.Value.GetTableClient("ToolboxSamples");
        await table.CreateIfNotExistsAsync(cancellationToken);
        var id = Guid.NewGuid().ToString("N");
        var entity = new TableEntity("samples", id) { ["Message"] = "Hello Azure Table Storage" };
        await table.AddEntityAsync(entity, cancellationToken);
        return await WithCleanupAsync(async () =>
        {
            var read = await table.GetEntityAsync<TableEntity>("samples", id, cancellationToken: cancellationToken);
            var text = read.Value.GetString("Message")!;
            Verify(text, "Hello Azure Table Storage");
            return text;
        }, cleanupToken => table.DeleteEntityAsync("samples", id, cancellationToken: cleanupToken));
    }

    /// <summary>Uploads/downloads a local S3 object from the initialized bucket, then deletes the object.</summary>
    public async Task<string> S3Async(CancellationToken cancellationToken = default)
    {
        var bucket = _configuration["Resources:S3:Bucket"] ?? "toolbox-samples";
        var key = $"samples/{Guid.NewGuid():N}.txt";
        await _s3.Value.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket, Key = key, ContentBody = "Hello Amazon S3", ContentType = "text/plain"
        }, cancellationToken);
        return await WithCleanupAsync(async () =>
        {
            using var read = await _s3.Value.GetObjectAsync(bucket, key, cancellationToken);
            using var reader = new StreamReader(read.ResponseStream);
            var text = await reader.ReadToEndAsync(cancellationToken);
            Verify(text, "Hello Amazon S3");
            return text;
        }, cleanupToken => _s3.Value.DeleteObjectAsync(bucket, key, cleanupToken));
    }

    // The vNext emulator requires gateway mode; certificate exceptions are restricted to its configured local endpoint.
    private CosmosClient CreateCosmosClient()
    {
        var endpoint = LocalEndpoint("Cosmos", "https://localhost:10081", "cosmosdb");
        var key = _configuration["Resources:Cosmos:Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Set Resources__Cosmos__Key to the local emulator key.");
        }
        var options = new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            RequestTimeout = TimeSpan.FromSeconds(15),
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions()
        };
        if (bool.TryParse(_configuration["Resources:Cosmos:AllowUntrustedLocalCertificate"], out var allow) && allow)
        {
            options.HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (request, _, _, errors) => errors == System.Net.Security.SslPolicyErrors.None
                    || (request.RequestUri is not null && request.RequestUri.Host == endpoint.Host && request.RequestUri.Port == endpoint.Port)
            }) { Timeout = TimeSpan.FromSeconds(15) };
        }
        return new CosmosClient(endpoint.AbsoluteUri, key, options);
    }

    // Redis multiplexers are shared across calls; the configured destination is checked before connecting.
    private async Task<ConnectionMultiplexer> CreateRedisAsync()
    {
        var options = ConfigurationOptions.Parse(_configuration["Resources:Redis:ConnectionString"] ?? "localhost:10120");
        foreach (var endpoint in options.EndPoints)
        {
            var host = endpoint switch { DnsEndPoint dns => dns.Host, IPEndPoint ip => ip.Address.ToString(), _ => throw new InvalidOperationException("Unsupported Redis endpoint.") };
            ValidateLocalUri(new UriBuilder("http", host).Uri, "redis");
        }
        options.AbortOnConnectFail = true;
        options.ConnectTimeout = 10000;
        options.AsyncTimeout = 10000;
        return await ConnectionMultiplexer.ConnectAsync(options);
    }

    // Table initialization handles concurrent calls and waits for ACTIVE with a finite retry budget.
    private async Task EnsureDynamoTableAsync(string table, CancellationToken cancellationToken)
    {
        try
        {
            await _dynamoDb.Value.CreateTableAsync(new CreateTableRequest
            {
                TableName = table,
                AttributeDefinitions = [new("Id", ScalarAttributeType.S)],
                KeySchema = [new("Id", KeyType.HASH)],
                BillingMode = BillingMode.PAY_PER_REQUEST
            }, cancellationToken);
        }
        catch (ResourceInUseException)
        {
            // A previous or concurrent invocation already initialized the shared local table.
        }
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await _dynamoDb.Value.DescribeTableAsync(table, cancellationToken);
            if (response.Table.TableStatus == TableStatus.ACTIVE)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException("DynamoDB Local table did not become active.");
    }

    // Explicit development credentials avoid ambient AWS credential/region discovery.
    private BasicAWSCredentials LocalCredentials()
    {
        return new BasicAWSCredentials(_configuration["Resources:AWS:AccessKey"] ?? "test", _configuration["Resources:AWS:SecretKey"] ?? "test");
    }

    // Settings are trusted developer configuration, while destinations remain constrained to toolbox emulators.
    private Uri LocalEndpoint(string service, string fallback, string containerHost)
    {
        var endpoint = new Uri(_configuration[$"Resources:{service}:Endpoint"] ?? fallback, UriKind.Absolute);
        ValidateLocalUri(endpoint, containerHost);
        return endpoint;
    }

    // Permit localhost or the one corresponding Compose hostname, never public cloud destinations.
    private static void ValidateLocalUri(Uri endpoint, string containerHost)
    {
        if ((endpoint.Scheme != "http" && endpoint.Scheme != "https") || (!endpoint.IsLoopback && endpoint.Host != containerHost) || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new InvalidOperationException($"Samples require a local emulator endpoint (localhost or {containerHost}).");
        }
    }

    // Azurite's well-known development account is used only when no explicit connection string is supplied.
    private string StorageConnectionString()
    {
        return _configuration["Resources:AzureStorage:ConnectionString"] ?? "UseDevelopmentStorage=true";
    }

    // Validate each storage SDK endpoint before any write or resource creation.
    private static T ValidateStorageClient<T>(T client) where T : class
    {
        var endpoint = client switch
        {
            BlobServiceClient blobs => blobs.Uri, QueueServiceClient queues => queues.Uri, TableServiceClient tables => tables.Uri,
            _ => throw new InvalidOperationException("Unsupported storage client.")
        };
        ValidateLocalUri(endpoint, "azurite");
        return client;
    }

    // Round trips fail visibly instead of reporting success when stored content differs.
    private static void Verify(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The sample read did not match the written content.");
        }
    }

    // Caller cancellation must not abandon sample cleanup or let a cleanup failure hide the original error.
    private static async Task<T> WithCleanupAsync<T>(Func<Task<T>> operation, Func<CancellationToken, Task> cleanup)
    {
        Exception? operationFailure = null;
        try
        {
            return await operation();
        }
        catch (Exception exception)
        {
            operationFailure = exception;
            throw;
        }
        finally
        {
            using var cleanupCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await cleanup(cleanupCancellation.Token);
            }
            catch (Exception cleanupFailure) when (operationFailure is not null)
            {
                throw new AggregateException("The sample operation and cleanup both failed.", operationFailure, cleanupFailure);
            }
        }
    }

    // Disposal can race a lazy connection; await it without blocking the thread that owns the host.
    private static async Task DisposeRedisAsync(Task<ConnectionMultiplexer> pendingConnection)
    {
        try
        {
            var connection = await pendingConnection.ConfigureAwait(false);
            connection.Dispose();
        }
        catch (Exception) when (pendingConnection.IsFaulted)
        {
            // The sample invocation observes initialization errors; failed initialization has no client to dispose.
        }
    }

    /// <summary>Disposes reusable SDK clients initialized by this sample instance.</summary>
    public void Dispose()
    {
        if (_cosmos.IsValueCreated) { _cosmos.Value.Dispose(); }
        if (_dynamoDb.IsValueCreated) { _dynamoDb.Value.Dispose(); }
        if (_s3.IsValueCreated) { _s3.Value.Dispose(); }
        if (_redis.IsValueCreated)
        {
            _ = DisposeRedisAsync(_redis.Value);
        }
    }
}
