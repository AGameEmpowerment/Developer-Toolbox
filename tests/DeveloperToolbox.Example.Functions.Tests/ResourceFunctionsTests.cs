using System.Net;
using System.Text.Json;

namespace DeveloperToolbox.Example.Functions.Tests;

public sealed class ResourceFunctionsTests
{
    public static IEnumerable<object[]> Routes()
    {
        foreach (var route in new[] { "sqlserver", "postgres", "cosmos", "dynamodb", "redis", "mail", "seq", "wiremock", "blob", "storage-queue", "table", "s3", "rabbitmq", "sqs", "sns", "eventbridge", "servicebus-queue", "servicebus-topic", "function-storage-queue" })
        {
            yield return [route];
        }
    }

    [TestCaseSource(nameof(Routes))]
    public async Task RunAsync_SupportedRoute_ReturnsJsonAndForwardsCancellation(string route)
    {
        var fake = new SampleFakes();
        using var context = new TestFunctionContext();
        using var cancellation = new CancellationTokenSource();
        var function = new ResourceFunctions(fake, fake, fake, fake);
        var response = await function.RunAsync(new TestHttpRequest(context), route, cancellation.Token);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.GetValues("Content-Type"), Has.Exactly(1).Items);
        Assert.That(response.Headers.GetValues("Content-Type").Single(), Does.StartWith("application/json"));
        Assert.That(fake.Calls, Has.Exactly(1).Items);
        Assert.That(fake.Calls.Single().Name, Is.EqualTo(route));
        Assert.That(fake.Calls[0].CancellationToken, Is.EqualTo(cancellation.Token));
        response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(response.Body);
        if (route is "sqlserver" or "postgres")
        {
            Assert.That(json.RootElement.GetProperty("Provider").GetString(), Is.EqualTo(route));
            Assert.That(json.RootElement.GetProperty("Inserted").GetProperty("Id").GetString(), Is.EqualTo("sample-id"));
        }
        else if (route is "servicebus-queue" or "servicebus-topic" or "function-storage-queue")
        {
            Assert.That(json.RootElement.GetProperty("Id").GetString(), Is.EqualTo("queue-id"));
            Assert.That(json.RootElement.GetProperty("Text").GetString(), Is.EqualTo(fake.Calls[0].Text));
        }
        else
        {
            Assert.That(json.RootElement.GetString(), Is.EqualTo($"result:{route}"));
        }
    }

    [TestCaseSource(nameof(Routes))]
    public async Task RunAsync_UppercaseRoute_UsesTheSameResource(string route)
    {
        var fake = new SampleFakes();
        using var context = new TestFunctionContext();
        var response = await new ResourceFunctions(fake, fake, fake, fake).RunAsync(new TestHttpRequest(context), route.ToUpperInvariant(), default);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(fake.Calls, Has.Exactly(1).Items);
        Assert.That(fake.Calls.Single().Name, Is.EqualTo(route));
    }

    [Test]
    public async Task RunAsync_UnknownRoute_Returns404WithoutInvokingResources()
    {
        var fake = new SampleFakes();
        using var context = new TestFunctionContext();
        var response = await new ResourceFunctions(fake, fake, fake, fake).RunAsync(new TestHttpRequest(context), "unknown", default);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(fake.Calls, Is.Empty);
        response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(response.Body);
        Assert.That(json.RootElement.GetProperty("error").GetString(), Is.EqualTo("Unknown toolbox resource."));
    }

    [TestCaseSource(nameof(Routes))]
    public async Task RunAsync_ResourceFailure_PropagatesInsteadOfReturningSuccess(string route)
    {
        var error = new IOException("resource failed");
        var fake = new SampleFakes { Failure = error };
        using var context = new TestFunctionContext();
        var thrown = await Assert.ThrowsAsync<IOException>(() => new ResourceFunctions(fake, fake, fake, fake).RunAsync(new TestHttpRequest(context), route, default));
        Assert.That(thrown, Is.SameAs(error));
    }

    [TestCaseSource(nameof(Routes))]
    public async Task RunAsync_CanceledInvocation_PropagatesTheInvocationToken(string route)
    {
        var fake = new SampleFakes();
        using var context = new TestFunctionContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.CatchAsync<OperationCanceledException>(() => new ResourceFunctions(fake, fake, fake, fake).RunAsync(new TestHttpRequest(context), route, cancellation.Token));
        Assert.That(fake.Calls, Has.Exactly(1).Items);
        Assert.That(fake.Calls.Single().CancellationToken, Is.EqualTo(cancellation.Token));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public async Task RunAsync_MissingRoute_RejectsBeforeCallingResources(string? route)
    {
        var fake = new SampleFakes();
        using var context = new TestFunctionContext();
        await Assert.CatchAsync<ArgumentException>(() => new ResourceFunctions(fake, fake, fake, fake).RunAsync(new TestHttpRequest(context), route!, default));
        Assert.That(fake.Calls, Is.Empty);
    }

    [Test]
    public async Task RunAsync_NullRequest_RejectsBeforeCallingResources()
    {
        var fake = new SampleFakes();
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ResourceFunctions(fake, fake, fake, fake).RunAsync(null!, "sqlserver", default));
        Assert.That(fake.Calls, Is.Empty);
    }
}
