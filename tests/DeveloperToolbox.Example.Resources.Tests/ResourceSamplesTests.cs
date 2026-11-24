using System.Net;
using System.Text.Json;
using DeveloperToolbox.Example.Resources;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace DeveloperToolbox.Example.Resources.Tests;

public sealed class ResourceSamplesTests
{
    public static IEnumerable<TestCaseData> RemoteDestinations => new TestCaseData[]
    {
        new("cosmos", "Resources:Cosmos:Endpoint", "https://example.documents.azure.com"),
        new("dynamodb", "Resources:DynamoDb:Endpoint", "https://dynamodb.us-east-1.amazonaws.com"),
        new("redis", "Resources:Redis:ConnectionString", "redis.example.test:6379"),
        new("mail", "Resources:Mail:Host", "smtp.example.test"),
        new("seq", "Resources:Seq:Endpoint", "https://seq.example.test"),
        new("wiremock", "Resources:WireMock:Endpoint", "https://wiremock.example.test"),
        new("blob", "Resources:AzureStorage:ConnectionString", CloudStorage),
        new("storage-queue", "Resources:AzureStorage:ConnectionString", CloudStorage),
        new("table", "Resources:AzureStorage:ConnectionString", CloudStorage),
        new("s3", "Resources:S3:Endpoint", "https://s3.us-east-1.amazonaws.com")
    };

    private static string CloudStorage => "DefaultEndpointsProtocol=https;AccountName=example;AccountKey="
        + Convert.ToBase64String(new byte[32]) + ";EndpointSuffix=core.windows.net";

    [TestCaseSource(nameof(RemoteDestinations))]
    public async Task ResourceAsync_RemoteDestination_RejectsBeforeConnecting(string command, string setting, string value)
    {
        using var samples = new ResourceSamples(Configuration((setting, value)), new RejectHttpFactory());

        var exception = (await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(samples, command)))!;

        Assert.That(exception.Message, Does.Contain("local emulator endpoint"));
    }

    [TestCase("seq", "Resources:Seq:Endpoint", "http://seq.example.test@localhost:10150")]
    [TestCase("wiremock", "Resources:WireMock:Endpoint", "ftp://localhost:10080")]
    [TestCase("cosmos", "Resources:Cosmos:Endpoint", "https://cosmosdb.example.test:10081")]
    [TestCase("s3", "Resources:S3:Endpoint", "https://aws-emulator.example.test:5000")]
    [TestCase("dynamodb", "Resources:DynamoDb:Endpoint", "file:///localhost")]
    [TestCase("redis", "Resources:Redis:ConnectionString", "localhost:10120,redis.example.test:6379")]
    public async Task ResourceAsync_UnsafeDestination_RejectsBeforeConnecting(string command, string setting, string value)
    {
        using var samples = new ResourceSamples(Configuration((setting, value)), new RejectHttpFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(samples, command));
    }

    [Test]
    public async Task CosmosAsync_MissingEmulatorKey_ExplainsInvokedSampleOnly()
    {
        using var samples = new ResourceSamples(Configuration(), new RejectHttpFactory());

        var failure = (await Assert.ThrowsAsync<InvalidOperationException>(() => samples.CosmosAsync()))!;

        Assert.That(failure.Message, Does.Contain("Resources__Cosmos__Key"));
    }

    [Test]
    public async Task SeqAsync_Success_SendsCorrelatedClefAndConfiguredKey()
    {
        string? sampleId = null;
        using var handler = new FakeHandler(async (request, _) =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.RequestUri!.AbsoluteUri, Is.EqualTo("http://otlp-seq/api/events/raw"));
            Assert.That(request.Content!.Headers.ContentType!.MediaType, Is.EqualTo("application/vnd.serilog.clef"));
            Assert.That(request.Headers.GetValues("X-Seq-ApiKey").Single(), Is.EqualTo("local-key"));
            var body = await request.Content.ReadAsStringAsync();
            Assert.That(body, Does.EndWith("\n"));
            using var json = JsonDocument.Parse(body);
            sampleId = json.RootElement.GetProperty("SampleId").GetString();
            Assert.That(Guid.TryParseExact(sampleId, "N", out var parsedId), Is.True);
            Assert.That(json.RootElement.GetProperty("Application").GetString(), Is.EqualTo("DeveloperToolbox.Example"));
            Assert.That(json.RootElement.GetProperty("@mt").GetString(), Is.EqualTo("Toolbox sample {SampleId}"));
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        using var samples = new ResourceSamples(Configuration(("Resources:Seq:Endpoint", "http://otlp-seq"),
            ("Resources:Seq:ApiKey", "local-key")), new FakeHttpFactory(handler));

        var result = await samples.SeqAsync();

        Assert.That(result, Does.Contain(sampleId!));
    }

    [Test]
    public async Task SeqAsync_ServerRejectsEvent_SurfacesHttpFailure()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        using var samples = new ResourceSamples(Configuration(), new FakeHttpFactory(handler));

        var failure = (await Assert.ThrowsAsync<HttpRequestException>(() => samples.SeqAsync()))!;

        Assert.That(failure.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("seq")]
    [TestCase("wiremock")]
    public async Task HttpSample_LocalRedirect_RejectsBeforeReportingSuccess(string resource)
    {
        var calls = 0;
        using var handler = new FakeHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("https://external.example.test/redirect");
            return Task.FromResult(response);
        });
        using var samples = new ResourceSamples(Configuration(("Resources:Seq:ApiKey", "local-key")), new FakeHttpFactory(handler));

        var failure = (await Assert.ThrowsAsync<HttpRequestException>(() => Invoke(samples, resource)))!;

        Assert.That(failure.StatusCode, Is.EqualTo(HttpStatusCode.TemporaryRedirect));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task SeqAsync_Cancellation_PropagatesToHttp()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new FakeHandler((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var samples = new ResourceSamples(Configuration(), new FakeHttpFactory(handler));

        await Assert.CatchAsync<OperationCanceledException>(() => samples.SeqAsync(cancellation.Token));
    }

    [Test]
    public async Task WireMockAsync_Success_RemovesExactlyTheCreatedMapping()
    {
        Guid createdId = default;
        var operations = new List<HttpMethod>();
        using var handler = new FakeHandler(async (request, token) =>
        {
            operations.Add(request.Method);
            if (request.Method == HttpMethod.Post)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                createdId = json.RootElement.GetProperty("id").GetGuid();
                Assert.That(json.RootElement.GetProperty("request").GetProperty("urlPath").GetString(), Is.EqualTo($"/toolbox-samples/{createdId:N}"));
                return new(HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Get)
            {
                Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo($"/toolbox-samples/{createdId:N}"));
                return new(HttpStatusCode.OK) { Content = new StringContent("Hello WireMock") };
            }
            Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo($"/__admin/mappings/{createdId}"));
            Assert.That(token.CanBeCanceled, Is.True);
            return new(HttpStatusCode.OK);
        });
        using var samples = new ResourceSamples(Configuration(), new FakeHttpFactory(handler));

        Assert.That(await samples.WireMockAsync(), Is.EqualTo("Hello WireMock"));
        Assert.That(operations, Is.EqualTo([HttpMethod.Post, HttpMethod.Get, HttpMethod.Delete]));
    }

    [Test]
    public async Task WireMockAsync_MismatchedRead_StillRemovesMapping()
    {
        var deleted = false;
        using var handler = new FakeHandler((request, _) =>
        {
            deleted |= request.Method == HttpMethod.Delete;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Wrong content") });
        });
        using var samples = new ResourceSamples(Configuration(), new FakeHttpFactory(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => samples.WireMockAsync());

        Assert.That(deleted, Is.True);
    }

    [Test]
    public async Task WireMockAsync_CanceledRead_CleansUpWithIndependentToken()
    {
        using var cancellation = new CancellationTokenSource();
        var deleted = false;
        using var handler = new FakeHandler((request, token) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            if (request.Method == HttpMethod.Delete)
            {
                deleted = true;
                Assert.That(token.IsCancellationRequested, Is.False);
                Assert.That(token.CanBeCanceled, Is.True);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var samples = new ResourceSamples(Configuration(), new FakeHttpFactory(handler));

        await Assert.CatchAsync<OperationCanceledException>(() => samples.WireMockAsync(cancellation.Token));

        Assert.That(deleted, Is.True);
    }

    [Test]
    public async Task WireMockAsync_CreateRejected_DoesNotDeleteOtherMappings()
    {
        var calls = 0;
        using var handler = new FakeHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        });
        using var samples = new ResourceSamples(Configuration(), new FakeHttpFactory(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => samples.WireMockAsync());

        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task WireMockAsync_ReadAndCleanupFail_PreservesBothFailures()
    {
        using var handler = new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(
            request.Method == HttpMethod.Delete ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        { Content = new StringContent("Wrong content") }));
        using var samples = new ResourceSamples(Configuration(), new FakeHttpFactory(handler));

        var failure = (await Assert.ThrowsAsync<AggregateException>(() => samples.WireMockAsync()))!;

        Assert.That(failure.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
        Assert.That(failure.InnerExceptions[1], Is.TypeOf<HttpRequestException>());
    }

    [Test]
    public async Task WireMockAsync_CleanupRejected_DoesNotReportSuccess()
    {
        using var handler = new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(
            request.Method == HttpMethod.Delete ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        { Content = new StringContent("Hello WireMock") }));
        using var samples = new ResourceSamples(Configuration(), new FakeHttpFactory(handler));

        var failure = (await Assert.ThrowsAsync<HttpRequestException>(() => samples.WireMockAsync()))!;

        Assert.That(failure.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    internal static IConfiguration Configuration(params (string Key, string Value)[] settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(x => x.Key, x => (string?)x.Value)).Build();

    private static Task<string> Invoke(IResourceSamples samples, string command) => command switch
    {
        "cosmos" => samples.CosmosAsync(), "dynamodb" => samples.DynamoDbAsync(), "redis" => samples.RedisAsync(),
        "mail" => samples.MailAsync(), "seq" => samples.SeqAsync(), "wiremock" => samples.WireMockAsync(),
        "blob" => samples.BlobAsync(), "storage-queue" => samples.StorageQueueAsync(), "table" => samples.TableAsync(),
        "s3" => samples.S3Async(), _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    internal sealed class RejectHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Unexpected HTTP client creation.");
    }

    private sealed class FakeHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.That(name, Is.EqualTo(ResourceSamples.HttpClientName));
            return new(handler, disposeHandler: false);
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
