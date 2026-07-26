using Microsoft.AspNetCore.Mvc;
using SystemUptimeTracker.Common.Helpers.Exceptions;
using SystemUptimeTracker.Api.Helpers.Tracing;

namespace SystemUptimeTracker.Api.Helpers.Middleware;

/// <summary>
/// Catches unhandled exceptions and returns scrubbed problem details that include a trace identifier.
/// </summary>
public class CustomExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CustomExceptionHandlerMiddleware> _logger;
    private readonly IProblemDetailsService _problemDetailsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomExceptionHandlerMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="problemDetailsService">The service used to write problem details responses.</param>
    /// <param name="logger">The exception logger.</param>
    public CustomExceptionHandlerMiddleware(
        RequestDelegate next,
        IProblemDetailsService problemDetailsService,
        ILogger<CustomExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _problemDetailsService = problemDetailsService;
    }

    /// <summary>
    /// Executes the middleware pipeline and converts unhandled exceptions into scrubbed problem details responses.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task representing the asynchronous middleware execution.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ProblemException)
        {
            throw;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            string traceId = RequestTraceContext.GetTraceId(context);

            _logger.LogError(
                ex,
                "An unhandled exception occurred. TraceId={TraceId}; RequestId={RequestId}; RequestMethod={RequestMethod}; RequestPath={RequestPath}; EndpointName={EndpointName}",
                traceId,
                context.TraceIdentifier,
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                context.GetEndpoint()?.DisplayName ?? string.Empty);

            ProblemDetails problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = RequestTraceContext.BuildUserMessage(
                    context,
                    "The server encountered an unexpected error."),
                Type = "Internal Server Error"
            };

            RequestTraceContext.EnrichProblemDetails(context, problemDetails);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problemDetails
            });
        }
    }
}
