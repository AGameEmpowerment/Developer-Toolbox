using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DeveloperToolbox.Example.Resources;
using NUnit.Framework;

namespace DeveloperToolbox.Example.Resources.Tests;

public sealed class AzureCleanupTests
{
    [Test]
    public async Task BlobAsync_VerifiedRead_DeletesOnlyCreatedBlobWithBoundedToken()
    {
        var blob = new FakeBlob();
        using var samples = Create(blob);

        Assert.That(await samples.BlobAsync(), Is.EqualTo("Hello Azure Blob Storage"));

        Assert.That(blob.Deleted, Is.True);
        Assert.That(blob.CleanupToken.CanBeCanceled, Is.True);
        Assert.That(blob.CreatedName, Does.StartWith("sample-"));
    }

    [Test]
    public async Task BlobAsync_CanceledRead_CleansUpWithIndependentToken()
    {
        using var cancellation = new CancellationTokenSource();
        var blob = new FakeBlob { Read = token => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return ""; } };
        using var samples = Create(blob);

        await Assert.CatchAsync<OperationCanceledException>(() => samples.BlobAsync(cancellation.Token));

        Assert.That(blob.Deleted, Is.True);
        Assert.That(blob.CleanupToken, Is.Not.EqualTo(cancellation.Token));
        Assert.That(blob.CleanupToken.IsCancellationRequested, Is.False);
    }

    [Test]
    public async Task BlobAsync_ReadAndCleanupFail_PreservesBothFailures()
    {
        var blob = new FakeBlob { Read = _ => "mismatched read", CleanupFailure = new RequestFailedException("Synthetic cleanup failure") };
        using var samples = Create(blob);

        var failure = (await Assert.ThrowsAsync<AggregateException>(() => samples.BlobAsync()))!;

        Assert.That(failure.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
        Assert.That(failure.InnerExceptions[1], Is.SameAs(blob.CleanupFailure));
    }

    private static ResourceSamples Create(FakeBlob blob)
        => new(ResourceSamplesTests.Configuration(), new ResourceSamplesTests.RejectHttpFactory(), null, null, new FakeService(blob));

    private sealed class FakeService(FakeBlob blob) : BlobServiceClient(new Uri("http://localhost:10000/devstoreaccount1"))
    {
        public override BlobContainerClient GetBlobContainerClient(string blobContainerName)
        {
            Assert.That(blobContainerName, Is.EqualTo("toolbox-samples"));
            return new FakeContainer(blob);
        }
    }

    private sealed class FakeContainer(FakeBlob blob) : BlobContainerClient(new Uri("http://localhost:10000/devstoreaccount1/toolbox-samples"))
    {
        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(PublicAccessType publicAccessType = PublicAccessType.None,
            IDictionary<string, string>? metadata = null, BlobContainerEncryptionScopeOptions? encryptionScopeOptions = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Response.FromValue<BlobContainerInfo>(null!, null!));

        public override BlobClient GetBlobClient(string blobName)
        {
            blob.CreatedName = blobName;
            return blob;
        }
    }

    private sealed class FakeBlob : BlobClient
    {
        public string CreatedName { get; set; } = "";
        public Func<CancellationToken, string> Read { get; set; } = _ => "Hello Azure Blob Storage";
        public Exception? CleanupFailure { get; set; }
        public bool Deleted { get; private set; }
        public CancellationToken CleanupToken { get; private set; }

        public override Task<Response<BlobContentInfo>> UploadAsync(BinaryData content, CancellationToken cancellationToken = default)
        {
            Assert.That(content.ToString(), Is.EqualTo("Hello Azure Blob Storage"));
            return Task.FromResult(Response.FromValue<BlobContentInfo>(null!, null!));
        }

        public override Task<Response<BlobDownloadResult>> DownloadContentAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Response.FromValue(BlobsModelFactory.BlobDownloadResult(BinaryData.FromString(Read(cancellationToken))), null!));

        public override Task<Response<bool>> DeleteIfExistsAsync(DeleteSnapshotsOption snapshotsOption = DeleteSnapshotsOption.IncludeSnapshots,
            BlobRequestConditions? conditions = null, CancellationToken cancellationToken = default)
        {
            Deleted = true;
            CleanupToken = cancellationToken;
            return CleanupFailure is null
                ? Task.FromResult(Response.FromValue(true, null!))
                : Task.FromException<Response<bool>>(CleanupFailure);
        }
    }
}
