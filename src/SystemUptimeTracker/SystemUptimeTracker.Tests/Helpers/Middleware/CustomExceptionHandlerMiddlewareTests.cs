using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SystemUptimeTracker.Common.Helpers.Exceptions;
using SystemUptimeTracker.Api.Helpers.Middleware;
using System.Diagnostics;

namespace SystemUptimeTracker.Tests.Helpers.Middleware;

[TestFixture(Category = "Unit")]
public class CustomExceptionHandlerMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_WhenUnhandledExceptionOccurs_ReturnsScrubbedProblemDetailsWithTraceId()
    {
        IProblemDetailsService problemDetailsService = Substitute.For<IProblemDetailsService>();
        ILogger<CustomExceptionHandlerMiddleware> logger = Substitute.For<ILogger<CustomExceptionHandlerMiddleware>>();
        ProblemDetailsContext? capturedContext = null;

        problemDetailsService
            .TryWriteAsync(Arg.Do<ProblemDetailsContext>(context => capturedContext = context))
            .Returns(new ValueTask<bool>(true));

        var middleware = new CustomExceptionHandlerMiddleware(
            _ => throw new InvalidOperationException("Sensitive server exception"),
            problemDetailsService,
            logger);
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "request-500"
        };

        using var activity = new Activity("UnhandledExceptionTest");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        await middleware.InvokeAsync(httpContext);

        Assert.That(capturedContext, Is.Not.Null);
        Assert.That(capturedContext!.ProblemDetails.Status, Is.EqualTo(StatusCodes.Status500InternalServerError));
        Assert.That(capturedContext.ProblemDetails.Title, Is.EqualTo("An unexpected error occurred."));
        Assert.That(capturedContext.ProblemDetails.Detail, Does.Contain(activity.TraceId.ToString()));
        Assert.That(capturedContext.ProblemDetails.Detail, Does.Not.Contain("Sensitive server exception"));
        Assert.That(capturedContext.ProblemDetails.Extensions["traceId"], Is.EqualTo(activity.TraceId.ToString()));
        Assert.That(capturedContext.ProblemDetails.Extensions["requestId"], Is.EqualTo("request-500"));
        Assert.That(httpContext.Response.Headers["X-Trace-Id"].ToString(), Is.EqualTo(activity.TraceId.ToString()));
    }

    [Test]
    public void InvokeAsync_WhenProblemExceptionOccurs_RethrowsForTheRegisteredExceptionHandler()
    {
        IProblemDetailsService problemDetailsService = Substitute.For<IProblemDetailsService>();
        ILogger<CustomExceptionHandlerMiddleware> logger = Substitute.For<ILogger<CustomExceptionHandlerMiddleware>>();
        var middleware = new CustomExceptionHandlerMiddleware(
            _ => throw new ProblemException("Validation detail"),
            problemDetailsService,
            logger);

        Assert.ThrowsAsync<ProblemException>(async () => await middleware.InvokeAsync(new DefaultHttpContext()));
        Assert.That(problemDetailsService.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public async Task InvokeAsync_WhenRequestIsAborted_DoesNotTranslateCancellationIntoA500()
    {
        IProblemDetailsService problemDetailsService = Substitute.For<IProblemDetailsService>();
        ILogger<CustomExceptionHandlerMiddleware> logger = Substitute.For<ILogger<CustomExceptionHandlerMiddleware>>();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var middleware = new CustomExceptionHandlerMiddleware(
            _ => throw new OperationCanceledException(cancellationSource.Token),
            problemDetailsService,
            logger);
        var httpContext = new DefaultHttpContext
        {
            RequestAborted = cancellationSource.Token
        };

        await middleware.InvokeAsync(httpContext);

        Assert.That(problemDetailsService.ReceivedCalls(), Is.Empty);
        Assert.That(logger.ReceivedCalls(), Is.Empty);
        Assert.That(httpContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
    }
}
