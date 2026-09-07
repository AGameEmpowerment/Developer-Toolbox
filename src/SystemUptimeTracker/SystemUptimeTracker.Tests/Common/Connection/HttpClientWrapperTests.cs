using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SystemUptimeTracker.Common.Connection;

namespace SystemUptimeTracker.Tests.Common.Connection;

[TestFixture(Category = "Unit")]
public sealed class HttpClientWrapperTests
{
    [Test]
    public async Task GetBytesAsync_SuccessfulResponse_ReturnsResponseBytes()
    {
        byte[] expected = [1, 2, 3, 4];
        using HttpClient client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expected)
        });
        IHttpClientFactory factory = CreateFactory(client);
        HttpClientWrapper wrapper = CreateWrapper(factory);

        byte[] result = await wrapper.GetBytesAsync("files/report.bin", "files");

        Assert.That(result, Is.EqualTo(expected));
        factory.Received(1).CreateClient("files");
    }

    [Test]
    public async Task GetObjectAsync_WithAuthorizationHeader_SendsExpectedGetRequestAndDeserializesResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        using HttpClient client = CreateClient(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"name\":\"toolbox\"}", Encoding.UTF8, "application/json")
            };
        });
        IHttpClientFactory factory = CreateFactory(client);
        HttpClientWrapper wrapper = CreateWrapper(factory);
        AuthenticationHeaderValue authorization = new("Bearer", "token");

        TestPayload? result = await wrapper.GetObjectAsync<TestPayload>(
            "items/42",
            "catalog",
            authorization);

        Assert.Multiple(() =>
        {
            Assert.That(result?.Name, Is.EqualTo("toolbox"));
            Assert.That(capturedRequest?.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(capturedRequest?.RequestUri, Is.EqualTo(new Uri("https://example.test/items/42")));
            Assert.That(capturedRequest?.Headers.Authorization, Is.EqualTo(authorization));
            Assert.That(capturedRequest?.Headers.Accept.Single().MediaType, Is.EqualTo("application/json"));
        });
        factory.Received(1).CreateClient("catalog");
    }

    [TestCase(HttpStatusCode.NoContent)]
    [TestCase(HttpStatusCode.NotFound)]
    public async Task GetObjectAsync_EmptyOrMissingResponse_ReturnsDefault(HttpStatusCode statusCode)
    {
        using HttpClient client = CreateClient(_ => new HttpResponseMessage(statusCode));
        HttpClientWrapper wrapper = CreateWrapper(CreateFactory(client));

        TestPayload? result = await wrapper.GetObjectAsync<TestPayload>("items/42", "catalog");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetObjectAsync_UnsuccessfulResponse_ThrowsTypedExceptionWithStatusCode()
    {
        using HttpClient client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        HttpClientWrapper wrapper = CreateWrapper(CreateFactory(client));

        HttpRequestException exception = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await wrapper.GetObjectAsync<TestPayload>("items/42", "catalog"))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(exception.Message, Does.Contain("items/42"));
        });
    }

    [Test]
    public void GetBytesAsync_CanceledRequest_PropagatesCancellation()
    {
        using HttpClient client = CreateClient((_, cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken));
        HttpClientWrapper wrapper = CreateWrapper(CreateFactory(client));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await wrapper.GetBytesAsync("files/report.bin", "files", cancellation.Token));
    }

    private static HttpClientWrapper CreateWrapper(IHttpClientFactory factory) =>
        new(NullLogger<HttpClientWrapper>.Instance, factory);

    private static IHttpClientFactory CreateFactory(HttpClient client)
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        return factory;
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) =>
        CreateClient((request, _) => Task.FromResult(responseFactory(request)));

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory))
        {
            BaseAddress = new Uri("https://example.test/")
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }

    private sealed class TestPayload
    {
        public string? Name { get; init; }
    }
}
