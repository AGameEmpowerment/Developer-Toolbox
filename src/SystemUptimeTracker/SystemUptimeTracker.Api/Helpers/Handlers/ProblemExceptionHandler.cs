using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SystemUptimeTracker.Common.Helpers.Exceptions;
using SystemUptimeTracker.Api.Helpers.Tracing;

namespace SystemUptimeTracker.Api.Helpers.Handlers;

/// <summary>
/// Handles <see cref="ProblemException"/> instances and converts them into scrubbed problem details responses.
/// </summary>
public class ProblemExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<ProblemExceptionHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProblemExceptionHandler"/> class.
    /// </summary>
    /// <param name="problemDetailsService">The service used to write problem details responses.</param>
    /// <param name="logger">The exception logger.</param>
    public ProblemExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<ProblemExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    /// <summary>
    /// Tries to convert the supplied exception into a scrubbed problem details response.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="exception">The exception to handle.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns><see langword="true"/> when the exception was handled; otherwise <see langword="false"/>.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ProblemException problemException)
        {
            return false;
        }

        string traceId = RequestTraceContext.GetTraceId(httpContext);

        _logger.LogWarning(
            problemException,
            "Handled problem exception. TraceId: {TraceId}; RequestMethod: {RequestMethod}; RequestPath: {RequestPath}",
            traceId,
            httpContext.Request.Method,
            httpContext.Request.Path.Value ?? string.Empty);

        ProblemDetails problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "The request could not be completed.",
            Detail = RequestTraceContext.BuildUserMessage(
                httpContext,
                "The request could not be processed."),
            Type = "Bad Request"
        };

        RequestTraceContext.EnrichProblemDetails(httpContext, problemDetails);
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        return await _problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails
            });
    }
}
