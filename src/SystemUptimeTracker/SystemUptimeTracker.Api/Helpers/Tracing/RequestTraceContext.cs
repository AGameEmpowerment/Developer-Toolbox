using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace SystemUptimeTracker.Api.Helpers.Tracing;

/// <summary>
/// Provides a consistent trace identifier for request-scoped logging and problem details responses.
/// </summary>
public static class RequestTraceContext
{
    /// <summary>
    /// Gets the problem-details extension key used for the request identifier.
    /// </summary>
    public const string REQUEST_ID_KEY = "requestId";

    /// <summary>
    /// Gets the problem-details extension key used for the trace identifier.
    /// </summary>
    public const string TRACE_ID_KEY = "traceId";

    /// <summary>
    /// Gets the structured logging key used for the backend module name.
    /// </summary>
    public const string MODULE_KEY = "module";

    /// <summary>
    /// Gets the structured logging key used for the current backend operation.
    /// </summary>
    public const string OPERATION_KEY = "operation";

    /// <summary>
    /// Gets the structured logging key used for the backend surface name.
    /// </summary>
    public const string SURFACE_KEY = "surface";

    /// <summary>
    /// Gets the structured logging key used for the current request path.
    /// </summary>
    public const string REQUEST_PATH_KEY = "requestPath";

    /// <summary>
    /// Gets the structured logging key used for the current request method.
    /// </summary>
    public const string REQUEST_METHOD_KEY = "requestMethod";

    /// <summary>
    /// Gets the structured logging key used for the resolved endpoint display name.
    /// </summary>
    public const string ENDPOINT_NAME_KEY = "endpointName";

    /// <summary>
    /// Gets the structured logging key used for the application name.
    /// </summary>
    public const string APPLICATION_NAME_KEY = "applicationName";

    /// <summary>
    /// Gets the structured logging key used for the environment name.
    /// </summary>
    public const string ENVIRONMENT_NAME_KEY = "environmentName";

    /// <summary>
    /// Gets the response header name used to expose the trace identifier to callers.
    /// </summary>
    public const string TRACE_ID_HEADER_NAME = "X-Trace-Id";

    private const string EMPTY_TRACE_ID = "00000000000000000000000000000000";
    private const string BACKEND_API_SURFACE = "backend-api";

    /// <summary>
    /// Resolves the canonical trace identifier for the current request.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The active distributed trace identifier, or the request identifier when no activity is available.</returns>
    public static string GetTraceId(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        Activity? activity = httpContext.Features.Get<IHttpActivityFeature>()?.Activity ?? Activity.Current;
        string? traceId = activity?.TraceId.ToString();

        return !string.IsNullOrWhiteSpace(traceId) && !string.Equals(traceId, EMPTY_TRACE_ID, StringComparison.Ordinal)
            ? traceId
            : httpContext.TraceIdentifier;
    }

    /// <summary>
    /// Creates a structured logging scope for the current request.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>A dictionary suitable for <see cref="ILogger.BeginScope{TState}(TState)"/>.</returns>
    public static IReadOnlyDictionary<string, object?> CreateLogScope(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var scope = new Dictionary<string, object?>
        {
            [TRACE_ID_KEY] = GetTraceId(httpContext),
            [REQUEST_ID_KEY] = httpContext.TraceIdentifier,
            [REQUEST_PATH_KEY] = httpContext.Request.Path.Value ?? string.Empty,
            [REQUEST_METHOD_KEY] = httpContext.Request.Method,
            [SURFACE_KEY] = BACKEND_API_SURFACE,
        };

        if (!string.IsNullOrWhiteSpace(httpContext.GetEndpoint()?.DisplayName))
        {
            scope[ENDPOINT_NAME_KEY] = httpContext.GetEndpoint()!.DisplayName;
        }

        IServiceProvider? requestServices = httpContext.RequestServices;
        IHostEnvironment? hostEnvironment = requestServices is null
            ? null
            : requestServices.GetService<IHostEnvironment>();
        if (hostEnvironment is not null)
        {
            scope[APPLICATION_NAME_KEY] = hostEnvironment.ApplicationName;
            scope[ENVIRONMENT_NAME_KEY] = hostEnvironment.EnvironmentName;
        }

        return scope;
    }

    /// <summary>
    /// Creates a structured logging scope for a specific backend module operation.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="moduleName">The backend module or controller name.</param>
    /// <param name="operationName">The current operation name.</param>
    /// <returns>A dictionary suitable for <see cref="ILogger.BeginScope{TState}(TState)"/>.</returns>
    public static IReadOnlyDictionary<string, object?> CreateLogScope(
        HttpContext httpContext,
        string moduleName,
        string operationName)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var scope = new Dictionary<string, object?>(CreateLogScope(httpContext))
        {
            [MODULE_KEY] = string.IsNullOrWhiteSpace(moduleName) ? string.Empty : moduleName.Trim(),
            [OPERATION_KEY] = string.IsNullOrWhiteSpace(operationName) ? string.Empty : operationName.Trim(),
        };

        return scope;
    }

    /// <summary>
    /// Creates a user-facing message that includes the current trace identifier.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="message">The generic message to present to the caller.</param>
    /// <returns>A scrubbed message containing the trace identifier.</returns>
    public static string BuildUserMessage(HttpContext httpContext, string message)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        string normalizedMessage = string.IsNullOrWhiteSpace(message)
            ? "The request could not be completed."
            : message.Trim();

        return $"{normalizedMessage} Trace ID: {GetTraceId(httpContext)}.";
    }

    /// <summary>
    /// Ensures the trace identifier response header is populated for the current request.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The trace identifier written to the response.</returns>
    public static string SetTraceIdHeader(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        string traceId = GetTraceId(httpContext);
        httpContext.Response.Headers[TRACE_ID_HEADER_NAME] = traceId;
        return traceId;
    }

    /// <summary>
    /// Adds request and trace identifiers to a <see cref="ProblemDetails"/> payload.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="problemDetails">The problem details payload to enrich.</param>
    public static void EnrichProblemDetails(HttpContext httpContext, ProblemDetails problemDetails)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(problemDetails);

        string traceId = SetTraceIdHeader(httpContext);

        problemDetails.Instance ??= $"{httpContext.Request.Method} {httpContext.Request.Path}";
        problemDetails.Extensions[TRACE_ID_KEY] = traceId;
        problemDetails.Extensions[REQUEST_ID_KEY] = httpContext.TraceIdentifier;
    }
}
