using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DeveloperToolbox.Example.Resources;
using NUnit.Framework;

namespace DeveloperToolbox.Example.Resources.Tests;

public sealed class AwsResourceSamplesTests
{
    [Test]
    public async Task S3Async_Success_UsesConfiguredBucketAndDeletesOnlyUploadedObject()
    {
        var fake = new FakeS3();
        AmazonS3Config? configuration = null;
        var calls = 0;
        using var samples = new ResourceSamples(ResourceSamplesTests.Configuration(("Resources:S3:Bucket", "custom-samples")),
            new ResourceSamplesTests.RejectHttpFactory(), null, config => { configuration = config; calls++; return fake; });

        Assert.That(await samples.S3Async(), Is.EqualTo("Hello Amazon S3"));
        Assert.That(await samples.S3Async(), Is.EqualTo("Hello Amazon S3"));

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(configuration!.ServiceURL, Is.EqualTo("http://localhost:10195/"));
        Assert.That(configuration.AuthenticationRegion, Is.EqualTo("us-east-1"));
        Assert.That(configuration.ForcePathStyle, Is.True);
        Assert.That(fake.Uploads.Count, Is.EqualTo(2));
        foreach (var item in fake.Uploads)
        {
            Assert.That(item.BucketName, Is.EqualTo("custom-samples"));
        }
        Assert.That(fake.Uploads.Select(item => item.Key).Distinct().Count(), Is.EqualTo(2));
        Assert.That(fake.DeletedKeys, Is.EqualTo(fake.Uploads.Select(item => item.Key)));
        foreach (var token in fake.CleanupTokens)
        {
            Assert.That(token.CanBeCanceled, Is.True);
        }
    }

    [Test]
    public async Task S3Async_MismatchedRead_DeletesOwnObjectAndFailsVerification()
    {
        var fake = new FakeS3 { Content = "different object" };
        using var samples = S3Samples(fake);

        await Assert.ThrowsAsync<InvalidOperationException>(() => samples.S3Async());

        Assert.That(fake.DeletedKeys.Single(), Is.EqualTo(fake.Uploads.Single().Key));
    }

    [Test]
    public async Task S3Async_CanceledRead_CleansUpAndPreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var fake = new FakeS3 { Read = token => { cancellation.Cancel(); return Task.FromCanceled<GetObjectResponse>(token); } };
        using var samples = S3Samples(fake);

        await Assert.CatchAsync<OperationCanceledException>(() => samples.S3Async(cancellation.Token));

        Assert.That(fake.DeletedKeys, Has.Exactly(1).Items);
        foreach (var token in fake.CleanupTokens)
        {
            Assert.That(token.IsCancellationRequested, Is.False);
        }
    }

    [Test]
    public async Task S3Async_UploadFails_DoesNotDeleteAnObjectItDidNotCreate()
    {
        var original = new AmazonS3Exception("upload failed");
        var fake = new FakeS3 { UploadFailure = original };
        using var samples = S3Samples(fake);

        Assert.That(await Assert.ThrowsAsync<AmazonS3Exception>(() => samples.S3Async()), Is.SameAs(original));
        Assert.That(fake.DeletedKeys, Is.Empty);
    }

    [Test]
    public async Task S3Async_ReadAndCleanupFail_SurfacesBothProviderFailures()
    {
        var read = new AmazonS3Exception("read failed");
        var cleanup = new AmazonS3Exception("delete failed");
        var fake = new FakeS3 { Read = _ => Task.FromException<GetObjectResponse>(read), CleanupFailure = cleanup };
        using var samples = S3Samples(fake);

        var failure = (await Assert.ThrowsAsync<AggregateException>(() => samples.S3Async()))!;

        Assert.That(failure.InnerExceptions, Is.EqualTo([read, cleanup]));
    }

    [Test]
    public async Task S3Async_CleanupFails_DoesNotReportSuccessfulRoundTrip()
    {
        var cleanup = new AmazonS3Exception("delete failed");
        var fake = new FakeS3 { CleanupFailure = cleanup };
        using var samples = S3Samples(fake);

        Assert.That(await Assert.ThrowsAsync<AmazonS3Exception>(() => samples.S3Async()), Is.SameAs(cleanup));
    }

    [Test]
    public async Task DynamoDbAsync_NewTable_CreatesStringPartitionKeyAndReadsConsistently()
    {
        var fake = new FakeDynamo();
        AmazonDynamoDBConfig? configuration = null;
        using var samples = new ResourceSamples(ResourceSamplesTests.Configuration(), new ResourceSamplesTests.RejectHttpFactory(),
            config => { configuration = config; return fake; }, null);

        Assert.That(await samples.DynamoDbAsync(), Does.Contain("Hello DynamoDB"));

        Assert.That(configuration!.ServiceURL, Is.EqualTo("http://localhost:10180/"));
        Assert.That(configuration.AuthenticationRegion, Is.EqualTo("us-east-1"));
        Assert.That(fake.CreatedTable!.BillingMode, Is.EqualTo(BillingMode.PAY_PER_REQUEST));
        Assert.That(fake.CreatedTable.KeySchema.Single().AttributeName, Is.EqualTo("Id"));
        Assert.That(fake.CreatedTable.KeySchema.Single().KeyType, Is.EqualTo(KeyType.HASH));
        Assert.That(fake.CreatedTable.AttributeDefinitions.Single().AttributeType, Is.EqualTo(ScalarAttributeType.S));
        Assert.That(fake.ReadRequest!.ConsistentRead, Is.True);
        Assert.That(fake.DeletedIds.Single(), Is.EqualTo(fake.WrittenItem!.Item["Id"].S));
        foreach (var token in fake.CleanupTokens)
        {
            Assert.That(token.CanBeCanceled, Is.True);
        }
    }

    [Test]
    public async Task DynamoDbAsync_ExistingTable_ReusesTableWithoutDeletingIt()
    {
        var fake = new FakeDynamo { CreateFailure = new ResourceInUseException("existing") };
        using var samples = DynamoSamples(fake);

        Assert.That(await samples.DynamoDbAsync(), Does.Contain("Hello DynamoDB"));

        Assert.That(fake.Operations, Is.EqualTo(["create", "describe", "put", "get", "delete-item"]));
    }

    [Test]
    public async Task DynamoDbAsync_CreateDenied_DoesNotContinueOrSuppressProviderFailure()
    {
        var denied = new AmazonDynamoDBException("access denied");
        var fake = new FakeDynamo { CreateFailure = denied };
        using var samples = DynamoSamples(fake);

        Assert.That(await Assert.ThrowsAsync<AmazonDynamoDBException>(() => samples.DynamoDbAsync()), Is.SameAs(denied));
        Assert.That(fake.Operations, Is.EqualTo(["create"]));
    }

    [Test]
    public async Task DynamoDbAsync_CanceledWhileTableIsCreating_DoesNotWriteAnItem()
    {
        using var cancellation = new CancellationTokenSource();
        var fake = new FakeDynamo { Status = TableStatus.CREATING, OnDescribe = cancellation.Cancel };
        using var samples = DynamoSamples(fake);

        await Assert.CatchAsync<OperationCanceledException>(() => samples.DynamoDbAsync(cancellation.Token));

        Assert.That(fake.Operations, Is.EqualTo(["create", "describe"]));
        Assert.That(fake.DeletedIds, Is.Empty);
    }

    [Test]
    public async Task DynamoDbAsync_MismatchedRead_RemovesOnlyOwnItem()
    {
        var fake = new FakeDynamo { Content = "different item" };
        using var samples = DynamoSamples(fake);

        await Assert.ThrowsAsync<InvalidOperationException>(() => samples.DynamoDbAsync());

        Assert.That(fake.DeletedIds.Single(), Is.EqualTo(fake.WrittenItem!.Item["Id"].S));
    }

    [Test]
    public async Task DynamoDbAsync_CanceledRead_CleansUpWithIndependentToken()
    {
        using var cancellation = new CancellationTokenSource();
        var fake = new FakeDynamo { Read = token => { cancellation.Cancel(); return Task.FromCanceled<GetItemResponse>(token); } };
        using var samples = DynamoSamples(fake);

        await Assert.CatchAsync<OperationCanceledException>(() => samples.DynamoDbAsync(cancellation.Token));

        Assert.That(fake.DeletedIds, Has.Exactly(1).Items);
        foreach (var token in fake.CleanupTokens)
        {
            Assert.That(token.IsCancellationRequested, Is.False);
        }
    }

    [Test]
    public async Task DynamoDbAsync_ReadAndCleanupFail_PreservesBothFailures()
    {
        var read = new AmazonDynamoDBException("read failed");
        var cleanup = new AmazonDynamoDBException("delete failed");
        var fake = new FakeDynamo { Read = _ => Task.FromException<GetItemResponse>(read), CleanupFailure = cleanup };
        using var samples = DynamoSamples(fake);

        var failure = (await Assert.ThrowsAsync<AggregateException>(() => samples.DynamoDbAsync()))!;

        Assert.That(failure.InnerExceptions, Is.EqualTo([read, cleanup]));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AwsResourceAsync_RemoteEndpoint_RejectsBeforeInvokingClientFactory(bool s3)
    {
        var called = false;
        using var samples = new ResourceSamples(ResourceSamplesTests.Configuration(
            (s3 ? "Resources:S3:Endpoint" : "Resources:DynamoDb:Endpoint", "https://aws.example.test")),
            new ResourceSamplesTests.RejectHttpFactory(), _ => { called = true; return new FakeDynamo(); },
            _ => { called = true; return new FakeS3(); });

        await Assert.ThrowsAsync<InvalidOperationException>(() => s3 ? samples.S3Async() : samples.DynamoDbAsync());

        Assert.That(called, Is.False);
    }

    private static ResourceSamples S3Samples(FakeS3 fake)
        => new(ResourceSamplesTests.Configuration(), new ResourceSamplesTests.RejectHttpFactory(), null, _ => fake);

    private static ResourceSamples DynamoSamples(FakeDynamo fake)
        => new(ResourceSamplesTests.Configuration(), new ResourceSamplesTests.RejectHttpFactory(), _ => fake, null);

    // These subclasses override every SDK operation reached by a sample; no HTTP transport is used.
    private sealed class FakeS3() : AmazonS3Client(new BasicAWSCredentials("test", "test"),
        new AmazonS3Config { ServiceURL = "http://localhost:10195", AuthenticationRegion = "us-east-1" })
    {
        public string Content { get; init; } = "Hello Amazon S3";
        public Exception? UploadFailure { get; init; }
        public Exception? CleanupFailure { get; init; }
        public Func<CancellationToken, Task<GetObjectResponse>>? Read { get; init; }
        public List<PutObjectRequest> Uploads { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public List<CancellationToken> CleanupTokens { get; } = [];

        public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            Uploads.Add(request);
            return UploadFailure is null ? Task.FromResult(new PutObjectResponse()) : Task.FromException<PutObjectResponse>(UploadFailure);
        }

        public override Task<GetObjectResponse> GetObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
        {
            Assert.That(bucketName, Is.EqualTo(Uploads[^1].BucketName));
            Assert.That(key, Is.EqualTo(Uploads[^1].Key));
            return Read?.Invoke(cancellationToken) ?? Task.FromResult(new GetObjectResponse
            { ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(Content)) });
        }

        public override Task<DeleteObjectResponse> DeleteObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
        {
            Assert.That(bucketName, Is.EqualTo(Uploads[^1].BucketName));
            DeletedKeys.Add(key);
            CleanupTokens.Add(cancellationToken);
            return CleanupFailure is null ? Task.FromResult(new DeleteObjectResponse()) : Task.FromException<DeleteObjectResponse>(CleanupFailure);
        }
    }

    private sealed class FakeDynamo() : AmazonDynamoDBClient(new BasicAWSCredentials("test", "test"),
        new AmazonDynamoDBConfig { ServiceURL = "http://localhost:10180", AuthenticationRegion = "us-east-1" })
    {
        public string Content { get; init; } = "Hello DynamoDB";
        public Exception? CreateFailure { get; init; }
        public Exception? CleanupFailure { get; init; }
        public Func<CancellationToken, Task<GetItemResponse>>? Read { get; init; }
        public TableStatus Status { get; init; } = TableStatus.ACTIVE;
        public Action? OnDescribe { get; init; }
        public CreateTableRequest? CreatedTable { get; private set; }
        public PutItemRequest? WrittenItem { get; private set; }
        public GetItemRequest? ReadRequest { get; private set; }
        public List<string> DeletedIds { get; } = [];
        public List<string> Operations { get; } = [];
        public List<CancellationToken> CleanupTokens { get; } = [];

        public override Task<CreateTableResponse> CreateTableAsync(CreateTableRequest request, CancellationToken cancellationToken = default)
        {
            Operations.Add("create");
            CreatedTable = request;
            return CreateFailure is null ? Task.FromResult(new CreateTableResponse()) : Task.FromException<CreateTableResponse>(CreateFailure);
        }

        public override Task<DescribeTableResponse> DescribeTableAsync(string tableName, CancellationToken cancellationToken = default)
        {
            Operations.Add("describe");
            Assert.That(tableName, Is.EqualTo(CreatedTable!.TableName));
            OnDescribe?.Invoke();
            return Task.FromResult(new DescribeTableResponse { Table = new() { TableStatus = Status } });
        }

        public override Task<PutItemResponse> PutItemAsync(PutItemRequest request, CancellationToken cancellationToken = default)
        {
            Operations.Add("put");
            WrittenItem = request;
            return Task.FromResult(new PutItemResponse());
        }

        public override Task<GetItemResponse> GetItemAsync(GetItemRequest request, CancellationToken cancellationToken = default)
        {
            Operations.Add("get");
            ReadRequest = request;
            Assert.That(request.Key["Id"].S, Is.EqualTo(WrittenItem!.Item["Id"].S));
            return Read?.Invoke(cancellationToken) ?? Task.FromResult(new GetItemResponse
            { Item = new() { ["Message"] = new() { S = Content } } });
        }

        public override Task<DeleteItemResponse> DeleteItemAsync(string tableName, Dictionary<string, AttributeValue> key,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("delete-item");
            Assert.That(tableName, Is.EqualTo(WrittenItem!.TableName));
            DeletedIds.Add(key["Id"].S);
            CleanupTokens.Add(cancellationToken);
            return CleanupFailure is null ? Task.FromResult(new DeleteItemResponse()) : Task.FromException<DeleteItemResponse>(CleanupFailure);
        }
    }
}
