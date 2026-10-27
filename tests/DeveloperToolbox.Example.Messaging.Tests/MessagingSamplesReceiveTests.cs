using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using DeveloperToolbox.Example.Messaging;
using NUnit.Framework;

namespace DeveloperToolbox.Example.Messaging.Tests;

public sealed class MessagingSamplesReceiveTests
{
    [TestCase("", "own-id")]
    [TestCase("   ", "own-id")]
    [TestCase("toolbox-work", "")]
    [TestCase("toolbox-work", "   ")]
    public async Task ReceiveAsync_MissingQueueOrCorrelation_RejectsBeforeReceiving(string queue, string correlation)
    {
        using var client = new FakeSqs();
        await Assert.ThrowsAsync<ArgumentException>(() => MessagingSamples.ReceiveAsync(client, queue, correlation, CancellationToken.None));

        Assert.That(client.ReceiveCount, Is.EqualTo(0));
        Assert.That(client.DeletedReceipts, Is.Empty);
    }

    [Test]
    public async Task ReceiveAsync_UnmatchedWork_HidesOnceUntilMatchingMessageAndRestoresEntireBatch()
    {
        using var client = new FakeSqs();
        var body = await MessagingSamples.ReceiveAsync(client, "toolbox-work", "own-id", CancellationToken.None);

        Assert.That(body, Is.EqualTo("own-id"));
        Assert.That(client.UnmatchedReceiveCount, Is.EqualTo(1));
        Assert.That(client.Operations, Is.EqualTo(new[] { "receive-1", "receive-2", "delete-own", "restore-unmatched", "restore-after-match" }));
        Assert.That(client.DeletedReceipts, Is.EqualTo(new[] { "own" }));
        foreach (var token in client.RestorationTokens)
        {
            Assert.That(token.CanBeCanceled, Is.True);
        }
    }

    [Test]
    public async Task ReceiveAsync_DuplicateCorrelatedDelivery_RestoresEveryUnsettledMessage()
    {
        using var client = new FakeSqs { ReturnDuplicateMatch = true };
        var body = await MessagingSamples.ReceiveAsync(client, "toolbox-work", "own-id", CancellationToken.None);

        Assert.That(body, Is.EqualTo("own-id"));
        Assert.That(client.DeletedReceipts, Is.EqualTo(new[] { "own" }));
        Assert.That(client.Operations, Does.Contain("restore-duplicate"));
        Assert.That(client.Operations, Does.Contain("restore-after-match"));
        Assert.That(client.RestorationTokens.Count, Is.EqualTo(3));
    }

    [TestCase("own-id", "prefix own-id suffix", "own-id")]
    [TestCase("own-id", "[\"own-id\"]", "own-id")]
    [TestCase("own-id", "{\"detail\":\"own-id\"}", "own-id")]
    [TestCase("own-id", "{\"detail\":{\"id\":128,\"message\":\"own-id\"}}", "own-id")]
    [TestCase("{\"id\":\"own-id\",\"message\":\"work\"}", "{\"copiedPayload\":{\"id\":\"own-id\",\"message\":\"work\"}}", "{\"id\":\"own-id\",\"message\":\"work\"}")]
    [TestCase("own-id", "{\"detail\":{\"id\":\"other-id\",\"message\":\"mentions own-id\"}}", "{\"detail\":{\"id\":\"own-id\",\"message\":\"work\"}}")]
    public async Task ReceiveAsync_UnrelatedBodyMentionsCorrelation_RestoresItAndDeletesOnlyMatchingEnvelope(
        string expected, string unrelated, string correlated)
    {
        using var client = new FakeSqs { UnmatchedBody = unrelated, MatchedBody = correlated };

        var body = await MessagingSamples.ReceiveAsync(client, "toolbox-work", expected, CancellationToken.None);

        Assert.That(body, Is.EqualTo(correlated));
        Assert.That(client.DeletedReceipts, Is.EqualTo(new[] { "own" }));
        Assert.That(client.Operations, Does.Contain("restore-unmatched"));
    }

    [Test]
    public async Task ReceiveAsync_MatchingMessageDoesNotArrive_RestoresUnmatchedWorkAfterTimeout()
    {
        using var client = new FakeSqs { ReturnMatch = false };
        await Assert.ThrowsAsync<TimeoutException>(() => MessagingSamples.ReceiveAsync(client, "toolbox-work", "own-id", CancellationToken.None));

        Assert.That(client.UnmatchedReceiveCount, Is.EqualTo(1));
        Assert.That(client.ReceiveCount, Is.EqualTo(5));
        Assert.That(client.Operations.Last(), Is.EqualTo("restore-unmatched"));
        Assert.That(client.RestorationTokens, Has.Exactly(1).Items);
        Assert.That(client.DeletedReceipts, Is.Empty);
    }

    [Test]
    public async Task ReceiveAsync_PollingIsCanceled_RestoresUnmatchedWorkAndPreservesCancellation()
    {
        using var caller = new CancellationTokenSource();
        using var client = new FakeSqs { ReceiveFailure = new OperationCanceledException(caller.Token) };
        var failure = (await Assert.ThrowsAsync<OperationCanceledException>(() =>
            MessagingSamples.ReceiveAsync(client, "toolbox-work", "own-id", caller.Token)))!;

        Assert.That(failure.CancellationToken, Is.EqualTo(caller.Token));
        Assert.That(client.Operations.Last(), Is.EqualTo("restore-unmatched"));
        Assert.That(client.RestorationTokens.Single(), Is.Not.EqualTo(caller.Token));
    }

    [Test]
    public async Task ReceiveAsync_PollingAndRestorationFail_ReportsBothProviderFailures()
    {
        var polling = new AmazonSQSException("Polling provider failure.");
        var restoration = new AmazonSQSException("Restoration provider failure.");
        using var client = new FakeSqs { ReceiveFailure = polling, RestorationFailure = restoration };
        var failure = (await Assert.ThrowsAsync<AggregateException>(() =>
            MessagingSamples.ReceiveAsync(client, "toolbox-work", "own-id", CancellationToken.None)))!;

        Assert.That(failure.InnerExceptions, Is.EqualTo(new Exception[] { polling, restoration }));
    }

    [Test]
    public async Task ReceiveAsync_RestorationFailsAfterMatch_ReportsProviderFailure()
    {
        var restoration = new AmazonSQSException("Restoration provider failure.");
        using var client = new FakeSqs { RestorationFailure = restoration };
        var failure = (await Assert.ThrowsAsync<AmazonSQSException>(() =>
            MessagingSamples.ReceiveAsync(client, "toolbox-work", "own-id", CancellationToken.None)))!;

        Assert.That(failure, Is.SameAs(restoration));
        Assert.That(client.Operations, Does.Contain("delete-own"));
    }

    // Model visibility so immediate resets would cause repeated receives of the older unrelated item.
    private sealed class FakeSqs : AmazonSQSClient
    {
        private bool unmatchedVisible = true;
        public bool ReturnMatch { get; init; } = true;
        public bool ReturnDuplicateMatch { get; init; }
        public string UnmatchedBody { get; init; } = "unrelated work";
        public string MatchedBody { get; init; } = "own-id";
        public Exception? ReceiveFailure { get; init; }
        public Exception? RestorationFailure { get; init; }
        public int ReceiveCount { get; private set; }
        public int UnmatchedReceiveCount { get; private set; }
        public List<string> Operations { get; } = [];
        public List<string> DeletedReceipts { get; } = [];
        public List<CancellationToken> RestorationTokens { get; } = [];

        public FakeSqs() : base(new BasicAWSCredentials("test", "test"), new AmazonSQSConfig
        {
            ServiceURL = "http://localhost:10195", AuthenticationRegion = "us-east-1"
        })
        {
        }

        public override Task<GetQueueUrlResponse> GetQueueUrlAsync(string queueName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GetQueueUrlResponse { QueueUrl = "http://localhost:10195/000000000000/" + queueName });
        }

        public override Task<ReceiveMessageResponse> ReceiveMessageAsync(ReceiveMessageRequest request, CancellationToken cancellationToken = default)
        {
            ReceiveCount++;
            Operations.Add($"receive-{ReceiveCount}");
            Assert.That(request.VisibilityTimeout, Is.EqualTo(60));
            if (ReceiveCount > 1 && ReceiveFailure is not null)
            {
                return Task.FromException<ReceiveMessageResponse>(ReceiveFailure);
            }
            if (unmatchedVisible)
            {
                unmatchedVisible = false;
                UnmatchedReceiveCount++;
                return Task.FromResult(new ReceiveMessageResponse
                {
                    Messages = [new() { Body = UnmatchedBody, ReceiptHandle = "unmatched" }]
                });
            }
            var messages = new List<Message>();
            if (ReturnMatch)
            {
                messages.Add(new() { Body = MatchedBody, ReceiptHandle = "own" });
                if (ReturnDuplicateMatch)
                {
                    messages.Add(new() { Body = "own-id", ReceiptHandle = "duplicate" });
                }
                messages.Add(new() { Body = "more unrelated work", ReceiptHandle = "after-match" });
            }
            return Task.FromResult(new ReceiveMessageResponse
            {
                Messages = messages
            });
        }

        public override Task<DeleteMessageResponse> DeleteMessageAsync(string queueUrl, string receiptHandle, CancellationToken cancellationToken = default)
        {
            Operations.Add("delete-" + receiptHandle);
            DeletedReceipts.Add(receiptHandle);
            return Task.FromResult(new DeleteMessageResponse());
        }

        public override Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(string queueUrl, string receiptHandle, int? visibilityTimeout, CancellationToken cancellationToken = default)
        {
            Assert.That(visibilityTimeout, Is.EqualTo(0));
            Operations.Add("restore-" + receiptHandle);
            RestorationTokens.Add(cancellationToken);
            unmatchedVisible = receiptHandle == "unmatched" || unmatchedVisible;
            return RestorationFailure is not null
                ? Task.FromException<ChangeMessageVisibilityResponse>(RestorationFailure)
                : Task.FromResult(new ChangeMessageVisibilityResponse());
        }
    }
}
