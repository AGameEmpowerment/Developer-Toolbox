using SystemUptimeTracker.Api.Helpers.Tracing;
using System.Diagnostics;

namespace SystemUptimeTracker.Api.Helpers.Middleware;

/// <summary>
/// Adds request-scoped trace identifiers to response headers and structured logging scopes.
/// </summary>
public class RequestTraceEnrichmentMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTraceEnrichmentMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestTraceEnrichmentMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The request trace logger.</param>
    public RequestTraceEnrichmentMiddleware(
        RequestDelegate next,
        ILogger<RequestTraceEnrichmentMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Adds the trace header and logging scope for the current request.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task representing the middleware pipeline.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        RequestTraceContext.SetTraceIdHeader(context);

        using IDisposable? scope = _logger.BeginScope(RequestTraceContext.CreateLogScope(context));
        ValueStopwatch stopwatch = ValueStopwatch.StartNew();

        _logger.LogDebug(
            "Request started. Method={RequestMethod}; Path={RequestPath}; EndpointName={EndpointName}.",
            context.Request.Method,
            context.Request.Path.Value ?? string.Empty,
            context.GetEndpoint()?.DisplayName ?? string.Empty);

        try
        {
            await _next(context);
        }
        finally
        {
            _logger.LogInformation(
                "Request completed. Method={RequestMethod}; Path={RequestPath}; EndpointName={EndpointName}; StatusCode={StatusCode}; DurationMs={DurationMs}.",
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                context.GetEndpoint()?.DisplayName ?? string.Empty,
                context.Response.StatusCode,
                stopwatch.GetElapsedTime().TotalMilliseconds);
        }
    }

    private readonly struct ValueStopwatch
    {
        private readonly long _startTimestamp;

        private ValueStopwatch(long startTimestamp)
        {
            _startTimestamp = startTimestamp;
        }

        public static ValueStopwatch StartNew()
        {
            return new ValueStopwatch(Stopwatch.GetTimestamp());
        }

        public TimeSpan GetElapsedTime()
        {
            long endTimestamp = Stopwatch.GetTimestamp();
            long timestampDelta = endTimestamp - _startTimestamp;
            double ticks = (double)timestampDelta / Stopwatch.Frequency * TimeSpan.TicksPerSecond;
            return new TimeSpan((long)ticks);
        }
    }
}
