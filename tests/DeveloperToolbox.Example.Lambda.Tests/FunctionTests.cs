using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using DeveloperToolbox.Example.Data;
using DeveloperToolbox.Example.Lambda;
using NUnit.Framework;
using NSubstitute;

namespace DeveloperToolbox.Example.Lambda.Tests;

public sealed class FunctionTests
{
    [TestCase(1)]
    [TestCase(4096)]
    public void ReadMessage_ValidBoundary_PreservesMessage(int length)
    {
        var expected = new string('x', length);
        Assert.That(Function.ReadMessage(JsonSerializer.Serialize(new { message = expected })), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ReadMessage_EmptyBody_RejectsInput(string? body)
    {
        Assert.Catch<ArgumentException>(() => Function.ReadMessage(body));
    }

    [Test]
    public void ReadMessage_MalformedJson_RejectsInput()
    {
        Assert.Catch<JsonException>(() => Function.ReadMessage("not JSON"));
    }

    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("{\"message\":123}")]
    [TestCase("{\"message\":null}")]
    [TestCase("{\"message\":\" \"}")]
    public void ReadMessage_InvalidContract_RejectsInput(string body)
    {
        Assert.Throws<ArgumentException>(() => Function.ReadMessage(body));
    }

    [Test]
    public void ReadMessage_TooLong_RejectsInput()
    {
        var body = JsonSerializer.Serialize(new { message = new string('x', 4097) });
        Assert.Throws<ArgumentException>(() => Function.ReadMessage(body));
    }

    [Test]
    public void HandleHttp_NullRequest_ReturnsBadRequest()
    {
        var response = new Function().HandleHttp(null, new TestLambdaContext());

        Assert.That(response.StatusCode, Is.EqualTo(400));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HandleHttp_OversizedBody_ReturnsBadRequest(bool isBase64)
    {
        var body = "{\"message\":\"Hello\",\"padding\":\"" + new string('x', 65536) + "\"}";
        var response = new Function().HandleHttp(new()
        {
            Body = isBase64 ? Convert.ToBase64String(Encoding.UTF8.GetBytes(body)) : body,
            IsBase64Encoded = isBase64
        }, new TestLambdaContext());

        Assert.That(response.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public void HandleHttp_Base64Body_ReturnsDecodedMessage()
    {
        var response = new Function().HandleHttp(new APIGatewayHttpApiV2ProxyRequest
        {
            IsBase64Encoded = true,
            Body = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"message\":\"Hello Lambda\"}"))
        }, new TestLambdaContext());
        Assert.That(response.StatusCode, Is.EqualTo(200));
        using var body = JsonDocument.Parse(response.Body);
        Assert.That(body.RootElement.GetProperty("message").GetString(), Is.EqualTo("Hello Lambda"));
    }

    [Test]
    public void HandleHttp_Base64WithInvalidUtf8_ReturnsBadRequest()
    {
        byte[] body = [.. Encoding.UTF8.GetBytes("{\"message\":\""), 0xff, .. Encoding.UTF8.GetBytes("\"}")];
        var response = new Function().HandleHttp(new()
        {
            IsBase64Encoded = true,
            Body = Convert.ToBase64String(body)
        }, new TestLambdaContext());

        Assert.That(response.StatusCode, Is.EqualTo(400));
    }

    [TestCase("not JSON", false)]
    [TestCase("invalid base64", true)]
    [TestCase(null, false)]
    public void HandleHttp_MalformedBody_ReturnsBadRequest(string? body, bool isBase64)
    {
        var response = new Function().HandleHttp(new APIGatewayHttpApiV2ProxyRequest
        {
            Body = body, IsBase64Encoded = isBase64
        }, new TestLambdaContext());
        Assert.That(response.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public void HandleEvent_EventBridgeEnvelope_ReadsDetail()
    {
        using var envelope = JsonDocument.Parse("{\"detail\":{\"message\":\"Hello events\"}}");
        Assert.That(new Function().HandleEvent(envelope.RootElement, new TestLambdaContext()), Is.EqualTo("Hello events"));
    }

    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("null")]
    [TestCase("{\"detail\":null}")]
    [TestCase("{\"detail\":[]}")]
    [TestCase("{\"detail\":{\"message\":123}}")]
    public void HandleEvent_InvalidEnvelope_RejectsInput(string json)
    {
        using var envelope = JsonDocument.Parse(json);
        Assert.Throws<ArgumentException>(() => new Function().HandleEvent(envelope.RootElement, new TestLambdaContext()));
    }

    [Test]
    public void HandleHttp_InvalidBody_ReturnsSafeErrorWithoutLoggingPayload()
    {
        var logger = new TestLambdaLogger();
        var context = new TestLambdaContext { Logger = logger };
        var response = new Function().HandleHttp(new() { Body = "fixture-secret-not-json" }, context);

        Assert.That(response.StatusCode, Is.EqualTo(400));
        Assert.That(response.Body, Does.Not.Contain("fixture-secret"));
        Assert.That(logger.Buffer.ToString(), Does.Not.Contain("fixture-secret"));
    }

    [Test]
    public void HandleHttp_ValidBody_LogsCompletionWithoutPayloadContents()
    {
        var logger = new TestLambdaLogger();
        var response = new Function().HandleHttp(new() { Body = "{\"message\":\"fixture-private-payload\"}" },
            new TestLambdaContext { Logger = logger });

        Assert.That(response.StatusCode, Is.EqualTo(200));
        Assert.That(logger.Buffer.ToString(), Does.Contain("Processed a local HTTP sample."));
        Assert.That(logger.Buffer.ToString(), Does.Not.Contain("fixture-private-payload"));
    }

    [Test]
    public void HandleEvent_ValidPayload_LogsWithoutPayloadContents()
    {
        var logger = new TestLambdaLogger();
        using var envelope = JsonDocument.Parse("{\"detail\":{\"message\":\"fixture-private-payload\"}}");
        new Function().HandleEvent(envelope.RootElement, new TestLambdaContext { Logger = logger });

        Assert.That(logger.Buffer.ToString(), Does.Contain("Processed a local EventBridge sample."));
        Assert.That(logger.Buffer.ToString(), Does.Not.Contain("fixture-private-payload"));
    }

    [Test]
    public async Task HandleSqs_ValidRecord_PersistsStableIdentityAndAcknowledges()
    {
        var (database, savedMessages) = CreateDatabase();
        var response = await new Function(database).HandleSqs(Batch(("stable-id", "{\"message\":\"queue work\"}")), QueueContext());

        Assert.That(savedMessages, Has.Exactly(1).Items);
        var saved = savedMessages.Single();
        Assert.That((saved.Id, saved.Text, saved.Source), Is.EqualTo(("stable-id", "queue work", "aws-sqs")));
        Assert.That(saved.Token.CanBeCanceled, Is.True);
        Assert.That(response.BatchItemFailures, Is.Empty);
    }

    [Test]
    public async Task HandleSqs_DuplicateDelivery_AcknowledgesAlreadyPersistedWork()
    {
        var (database, savedMessages) = CreateDatabase(_ => Task.FromResult(false));
        var response = await new Function(database).HandleSqs(Batch(("duplicate", "{\"message\":\"queue work\"}")), QueueContext());

        Assert.That(response.BatchItemFailures, Is.Empty);
    }

    [Test]
    public async Task HandleSqs_MixedBatch_ContinuesAndRetriesOnlyFailedRecords()
    {
        var (database, savedMessages) = CreateDatabase(saved => saved.Id == "db-failure" ? Task.FromException<bool>(new InvalidOperationException("fixture-private-connection")) : Task.FromResult(true));
        var logger = new TestLambdaLogger();
        var response = await new Function(database).HandleSqs(Batch(
            ("first-good", "{\"message\":\"first\"}"), ("bad-json", "fixture-private-payload"),
            ("db-failure", "{\"message\":\"second\"}"), ("last-good", "{\"message\":\"last\"}")),
            new TestLambdaContext { Logger = logger, RemainingTime = TimeSpan.FromSeconds(30) });

        Assert.That(response.BatchItemFailures.Select(item => item.ItemIdentifier), Is.EqualTo(new[] { "bad-json", "db-failure" }));
        Assert.That(savedMessages.Select(item => item.Id), Is.EqualTo(new[] { "first-good", "db-failure", "last-good" }));
        Assert.That(logger.Buffer.ToString(), Does.Contain("Persisted a local SQS sample."));
        Assert.That(logger.Buffer.ToString(), Does.Contain("SQS sample failed ("));
        Assert.That(logger.Buffer.ToString(), Does.Contain("SQS sample failed (InvalidOperationException); it will be retried."));
        Assert.That(logger.Buffer.ToString(), Does.Not.Contain("fixture-private"));
    }

    [Test]
    public async Task HandleSqs_CanceledPersistence_ReportsRetryAndContinuesBatch()
    {
        var (database, savedMessages) = CreateDatabase(saved => saved.Id == "canceled" ? Task.FromCanceled<bool>(new CancellationToken(true)) : Task.FromResult(true));
        var response = await new Function(database).HandleSqs(Batch(
            ("canceled", "{\"message\":\"first\"}"), ("good", "{\"message\":\"second\"}")), QueueContext());

        Assert.That(response.BatchItemFailures, Has.Exactly(1).Items);
        Assert.That(response.BatchItemFailures.Single().ItemIdentifier, Is.EqualTo("canceled"));
        Assert.That(savedMessages.Select(item => item.Id), Is.EqualTo(new[] { "canceled", "good" }));
    }

    [Test]
    public async Task HandleSqs_EmptyBatch_AcknowledgesWithoutResolvingDatabase()
    {
        var response = await new Function().HandleSqs(new() { Records = [] }, new TestLambdaContext());
        Assert.That(response.BatchItemFailures, Is.Empty);
    }

    [TestCase(0)]
    [TestCase(500)]
    [TestCase(1000)]
    public async Task HandleSqs_InsufficientExecutionTime_RetriesWithoutStartingDatabaseWork(int milliseconds)
    {
        var (database, savedMessages) = CreateDatabase();
        var context = new TestLambdaContext { RemainingTime = TimeSpan.FromMilliseconds(milliseconds) };
        var response = await new Function(database).HandleSqs(Batch(
            ("first", "{\"message\":\"work\"}"), ("second", "{\"message\":\"more work\"}")), context);

        Assert.That(response.BatchItemFailures.Select(item => item.ItemIdentifier), Is.EqualTo(new[] { "first", "second" }));
        Assert.That(savedMessages, Is.Empty);
    }

    [Test]
    public async Task HandleSqs_ExecutionBudgetExpires_RetriesInflightAndUnstartedRecords()
    {
        var (database, savedMessages) = CreateDatabase(async saved => { await Task.Delay(Timeout.InfiniteTimeSpan, saved.Token); return true; });
        var context = new TestLambdaContext { RemainingTime = TimeSpan.FromMilliseconds(1100) };
        var response = await new Function(database).HandleSqs(Batch(
            ("first", "{\"message\":\"work\"}"), ("second", "{\"message\":\"more work\"}")), context)
            .WaitAsync(TimeSpan.FromSeconds(3));

        Assert.That(response.BatchItemFailures.Select(item => item.ItemIdentifier), Is.EqualTo(new[] { "first", "second" }));
        Assert.That(savedMessages, Has.Exactly(1).Items);
        Assert.That(savedMessages.Single().Token.IsCancellationRequested, Is.True);
    }

    [Test]
    public async Task HandleSqs_PersistenceReturnsAfterBudgetCancellation_RetriesWithoutSuccessLog()
    {
        var (database, savedMessages) = CreateDatabase(async saved =>
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, saved.Token); }
                catch (OperationCanceledException) when (saved.Token.IsCancellationRequested) { }
                return true;
            });
        var logger = new TestLambdaLogger();
        var context = new TestLambdaContext
        {
            RemainingTime = TimeSpan.FromMilliseconds(1100), Logger = logger
        };
        var response = await new Function(database).HandleSqs(
            Batch(("late", "{\"message\":\"work\"}"), ("unstarted", "{\"message\":\"more work\"}")), context)
            .WaitAsync(TimeSpan.FromSeconds(3));

        Assert.That(response.BatchItemFailures.Select(item => item.ItemIdentifier), Is.EqualTo(new[] { "late", "unstarted" }));
        Assert.That(savedMessages, Has.Exactly(1).Items);
        Assert.That(savedMessages.Single().Token.IsCancellationRequested, Is.True);
        Assert.That(logger.Buffer.ToString(), Does.Not.Contain("Persisted a local SQS sample."));
    }

    [Test]
    public async Task HandleSqs_NullBatch_RejectsInput()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new Function().HandleSqs(null!, QueueContext()));
    }

    [TestCase(128, false)]
    [TestCase(129, true)]
    public async Task HandleSqs_MessageIdBoundary_RejectsOnlyOversizedIdentity(int length, bool rejected)
    {
        var (database, savedMessages) = CreateDatabase();
        var response = await new Function(database).HandleSqs(Batch((new string('x', length), "{\"message\":\"work\"}")), QueueContext());

        Assert.That(response.BatchItemFailures.Count, Is.EqualTo(rejected ? 1 : 0));
        Assert.That(savedMessages.Count, Is.EqualTo(rejected ? 0 : 1));
    }


    [Test]
    public async Task HandleSqs_InvalidRecords_ReportsEachFailedMessage()
    {
        var response = await new Function().HandleSqs(new SQSEvent
        {
            Records = [new() { MessageId = "bad-json", Body = "not JSON" },
                new() { MessageId = "bad-contract", Body = "{}" }]
        }, QueueContext());
        Assert.That(response.BatchItemFailures.Select(x => x.ItemIdentifier), Is.EqualTo(new[] { "bad-json", "bad-contract" }));
    }

    [Test]
    public async Task HandleSqs_MissingMessageId_RejectsUnreportableFailure()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new Function().HandleSqs(new SQSEvent
        {
            Records = [new() { MessageId = "", Body = "{}" }]
        }, QueueContext()));
    }

    private static TestLambdaContext QueueContext() => new() { RemainingTime = TimeSpan.FromSeconds(30) };

    // Construct records without environment variables or database services.
    private static SQSEvent Batch(params (string Id, string Body)[] records) => new()
    {
        Records = records.Select(record => new SQSEvent.SQSMessage { MessageId = record.Id, Body = record.Body }).ToList()
    };

    private sealed record SavedMessage(string Id, string Text, string Source, CancellationToken Token);

    // NSubstitute isolates persistence and records the exact values and budget token sent by Lambda.
    private static (IDatabaseSamples Database, List<SavedMessage> Saved) CreateDatabase(Func<SavedMessage, Task<bool>>? save = null)
    {
        var database = Substitute.For<IDatabaseSamples>();
        var saved = new List<SavedMessage>();
        database.SavePostgresMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var message = new SavedMessage(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<string>(2), call.Arg<CancellationToken>());
                saved.Add(message);
                return save is null ? Task.FromResult(true) : save(message);
            });
        // Unexpected providers must fail rather than appear to persist successfully.
        database.SaveSqlServerMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<bool>(new AssertionException("Lambda SQS work must use PostgreSQL.")));
        database.PostgresAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<DatabaseSampleResult>(new AssertionException("Lambda must save individual messages.")));
        database.SqlServerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<DatabaseSampleResult>(new AssertionException("Lambda must save individual messages.")));
        return (database, saved);
    }
}
