using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SystemUptimeTracker.Api.Helpers.Middleware;
using System.Diagnostics;

namespace SystemUptimeTracker.Tests.Helpers.Middleware;

[TestFixture(Category = "Unit")]
public class RequestTraceEnrichmentMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_WhenRequestCompletes_AddsTraceIdHeader()
    {
        ILogger<RequestTraceEnrichmentMiddleware> logger = Substitute.For<ILogger<RequestTraceEnrichmentMiddleware>>();
        var middleware = new RequestTraceEnrichmentMiddleware(
            async context => await context.Response.StartAsync(),
            logger);
        var httpContext = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            },
            TraceIdentifier = "request-123"
        };

        using var activity = new Activity("RequestTraceTest");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        await middleware.InvokeAsync(httpContext);
        await httpContext.Response.CompleteAsync();

        Assert.That(
            httpContext.Response.Headers["X-Trace-Id"].ToString(),
            Is.EqualTo(activity.TraceId.ToString()));
    }

    [Test]
    public void InvokeAsync_WhenNextThrows_StillEmitsCompletionLog()
    {
        ILogger<RequestTraceEnrichmentMiddleware> logger = Substitute.For<ILogger<RequestTraceEnrichmentMiddleware>>();
        var middleware = new RequestTraceEnrichmentMiddleware(
            _ => throw new InvalidOperationException("boom"),
            logger);
        var httpContext = new DefaultHttpContext();

        Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(httpContext));

        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state != null && state.ToString()!.Contains("Request completed.")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
