using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SystemUptimeTracker.Common.Helpers.Exceptions;
using SystemUptimeTracker.Api.Helpers.Handlers;
using System.Diagnostics;

namespace SystemUptimeTracker.Tests.Helpers.Handlers;

[TestFixture(Category = "Unit")]
public class ProblemExceptionHandlerTests
{
    [Test]
    public async Task TryHandleAsync_WhenExceptionIsProblemException_ReturnsScrubbedProblemDetailsWithTraceId()
    {
        IProblemDetailsService problemDetailsService = Substitute.For<IProblemDetailsService>();
        ILogger<ProblemExceptionHandler> logger = Substitute.For<ILogger<ProblemExceptionHandler>>();
        ProblemDetailsContext? capturedContext = null;

        problemDetailsService
            .TryWriteAsync(Arg.Do<ProblemDetailsContext>(context => capturedContext = context))
            .Returns(new ValueTask<bool>(true));

        var handler = new ProblemExceptionHandler(problemDetailsService, logger);
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "request-400"
        };

        using var activity = new Activity("ProblemExceptionTest");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        bool handled = await handler.TryHandleAsync(
            httpContext,
            new ProblemException("Sensitive validation detail"),
            CancellationToken.None);

        Assert.That(handled, Is.True);
        Assert.That(capturedContext, Is.Not.Null);
        Assert.That(capturedContext!.ProblemDetails.Status, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(capturedContext.ProblemDetails.Title, Is.EqualTo("The request could not be completed."));
        Assert.That(capturedContext.ProblemDetails.Detail, Does.Contain(activity.TraceId.ToString()));
        Assert.That(capturedContext.ProblemDetails.Detail, Does.Not.Contain("Sensitive validation detail"));
        Assert.That(capturedContext.ProblemDetails.Extensions["traceId"], Is.EqualTo(activity.TraceId.ToString()));
        Assert.That(capturedContext.ProblemDetails.Extensions["requestId"], Is.EqualTo("request-400"));
        Assert.That(httpContext.Response.Headers["X-Trace-Id"].ToString(), Is.EqualTo(activity.TraceId.ToString()));
    }

    [Test]
    public async Task TryHandleAsync_WhenExceptionIsNotProblemException_ReturnsFalse()
    {
        IProblemDetailsService problemDetailsService = Substitute.For<IProblemDetailsService>();
        ILogger<ProblemExceptionHandler> logger = Substitute.For<ILogger<ProblemExceptionHandler>>();
        var handler = new ProblemExceptionHandler(problemDetailsService, logger);

        bool handled = await handler.TryHandleAsync(
            new DefaultHttpContext(),
            new InvalidOperationException("Unexpected"),
            CancellationToken.None);

        Assert.That(handled, Is.False);
        Assert.That(problemDetailsService.ReceivedCalls(), Is.Empty);
    }
}
