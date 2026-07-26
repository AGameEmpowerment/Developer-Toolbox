using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SystemUptimeTracker.Api.Helpers.Middleware;
using SystemUptimeTracker.Api.Helpers.Tracing;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace SystemUptimeTracker.Tests.Helpers.Middleware;

[TestFixture(Category = "Integration")]
public sealed class RequestTracingAndProblemDetailsIntegrationTests
{
    [Test]
    public async Task RequestPipeline_WhenRequestSucceeds_EmitsTraceHeaderAndStructuredCompletionLog()
    {
        var logSink = new InMemoryLogSink();
        await using WebApplication app = await CreateApplicationAsync(logSink);

        ILogger<RequestTraceEnrichmentMiddleware> middlewareLogger = app.Services.GetRequiredService<ILogger<RequestTraceEnrichmentMiddleware>>();
        Assert.That(middlewareLogger.IsEnabled(LogLevel.Information), Is.True);

        HttpClient client = app.GetTestClient();
        HttpResponseMessage response = await client.GetAsync("/test/ping");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        string? traceId = GetSingleHeaderValue(response, RequestTraceContext.TRACE_ID_HEADER_NAME);
        Assert.That(traceId, Is.Not.Null.And.Not.Empty);

        InMemoryLogSink.LogEntry? completionLog = logSink.Entries.SingleOrDefault(entry =>
            entry.Category == typeof(RequestTraceEnrichmentMiddleware).FullName &&
            entry.Message.Contains("Request completed.", StringComparison.Ordinal));

        Assert.That(
            completionLog,
            Is.Not.Null,
            string.Join(Environment.NewLine, logSink.Entries.Select(entry => $"{entry.Level} {entry.Category}: {entry.Message}")));

        Assert.Multiple(() =>
        {
            Assert.That(completionLog!.Level, Is.EqualTo(LogLevel.Information));
            Assert.That(completionLog.ScopeValues.TryGetValue(RequestTraceContext.TRACE_ID_KEY, out object? scopedTraceId), Is.True);
            Assert.That(scopedTraceId?.ToString(), Is.EqualTo(traceId));
            Assert.That(completionLog.ScopeValues[RequestTraceContext.REQUEST_ID_KEY]?.ToString(), Is.Not.Null.And.Not.Empty);
            Assert.That(completionLog.ScopeValues[RequestTraceContext.REQUEST_METHOD_KEY]?.ToString(), Is.EqualTo("GET"));
            Assert.That(completionLog.ScopeValues[RequestTraceContext.REQUEST_PATH_KEY]?.ToString(), Is.EqualTo("/test/ping"));
            Assert.That(completionLog.ScopeValues[RequestTraceContext.SURFACE_KEY]?.ToString(), Is.EqualTo("backend-api"));
            Assert.That(completionLog.ScopeValues[RequestTraceContext.APPLICATION_NAME_KEY]?.ToString(), Is.Not.Null.And.Not.Empty);
            Assert.That(completionLog.ScopeValues[RequestTraceContext.ENVIRONMENT_NAME_KEY]?.ToString(), Is.EqualTo(Environments.Development));
            Assert.That(completionLog.ScopeValues[RequestTraceContext.ENDPOINT_NAME_KEY]?.ToString(), Does.Contain("/test/ping"));
        });
    }

    [Test]
    public async Task RequestPipeline_WhenEndpointThrows_ReturnsScrubbedProblemDetailsWithMatchingTraceAndLogsError()
    {
        const string INTERNAL_ERROR = "super-secret-diagnostic-detail";

        var logSink = new InMemoryLogSink();
        await using WebApplication app = await CreateApplicationAsync(logSink, INTERNAL_ERROR);

        HttpClient client = app.GetTestClient();
        HttpResponseMessage response = await client.GetAsync("/test/fail");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));

        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument json = JsonDocument.Parse(payload);

        string? traceIdHeader = GetSingleHeaderValue(response, RequestTraceContext.TRACE_ID_HEADER_NAME);
        string? traceIdBody = json.RootElement
            .GetProperty("traceId")
            .GetString();
        string? detail = json.RootElement
            .GetProperty("detail")
            .GetString();
        string? title = json.RootElement
            .GetProperty("title")
            .GetString();

        Assert.Multiple(() =>
        {
            Assert.That(traceIdHeader, Is.Not.Null.And.Not.Empty);
            Assert.That(traceIdBody, Is.EqualTo(traceIdHeader));
            Assert.That(title, Is.EqualTo("An unexpected error occurred."));
            Assert.That(detail, Does.Contain("Trace ID:"));
            Assert.That(detail, Does.Not.Contain(INTERNAL_ERROR));
            Assert.That(payload, Does.Not.Contain(INTERNAL_ERROR));
        });

        InMemoryLogSink.LogEntry errorLog = logSink.Entries.Single(entry =>
            entry.Category == typeof(CustomExceptionHandlerMiddleware).FullName &&
            entry.Level == LogLevel.Error);

        Assert.Multiple(() =>
        {
            Assert.That(errorLog.Exception, Is.Not.Null);
            Assert.That(errorLog.Exception!.Message, Is.EqualTo(INTERNAL_ERROR));
            Assert.That(errorLog.ScopeValues.TryGetValue(RequestTraceContext.TRACE_ID_KEY, out object? scopedTraceId), Is.True);
            Assert.That(scopedTraceId?.ToString(), Is.EqualTo(traceIdHeader));
            Assert.That(errorLog.ScopeValues[RequestTraceContext.REQUEST_ID_KEY]?.ToString(), Is.Not.Null.And.Not.Empty);
            Assert.That(errorLog.ScopeValues[RequestTraceContext.REQUEST_METHOD_KEY]?.ToString(), Is.EqualTo("GET"));
            Assert.That(errorLog.ScopeValues[RequestTraceContext.REQUEST_PATH_KEY]?.ToString(), Is.EqualTo("/test/fail"));
            Assert.That(errorLog.ScopeValues[RequestTraceContext.SURFACE_KEY]?.ToString(), Is.EqualTo("backend-api"));
            Assert.That(errorLog.ScopeValues[RequestTraceContext.APPLICATION_NAME_KEY]?.ToString(), Is.Not.Null.And.Not.Empty);
            Assert.That(errorLog.ScopeValues[RequestTraceContext.ENVIRONMENT_NAME_KEY]?.ToString(), Is.EqualTo(Environments.Development));
            Assert.That(errorLog.ScopeValues[RequestTraceContext.ENDPOINT_NAME_KEY]?.ToString(), Does.Contain("/test/fail"));
        });
    }

    private static async Task<WebApplication> CreateApplicationAsync(InMemoryLogSink logSink, string exceptionMessage = "ignored")
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                RequestTraceContext.EnrichProblemDetails(context.HttpContext, context.ProblemDetails);
            };
        });

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddProvider(logSink);
        builder.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            options.MinLevel = LogLevel.Debug;
            options.Rules.Clear();
        });

        WebApplication app = builder.Build();
        app.UseMiddleware<RequestTraceEnrichmentMiddleware>();
        app.UseMiddleware<CustomExceptionHandlerMiddleware>();
        app.MapGet("/test/ping", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/test/fail", (HttpContext _) => throw new InvalidOperationException(exceptionMessage));
        await app.StartAsync();
        return app;
    }

    private static string? GetSingleHeaderValue(HttpResponseMessage response, string headerName)
    {
        return response.Headers.TryGetValues(headerName, out IEnumerable<string>? values)
            ? values.Single()
            : null;
    }

    private sealed class InMemoryLogSink : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName)
        {
            return new InMemoryLogger(categoryName, Entries, () => _scopeProvider);
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public void Dispose()
        {
        }

        public sealed record LogEntry(
            string Category,
            LogLevel Level,
            string Message,
            Exception? Exception,
            IReadOnlyDictionary<string, object?> ScopeValues);

        private sealed class InMemoryLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly ConcurrentQueue<LogEntry> _entries;
            private readonly Func<IExternalScopeProvider> _scopeProviderAccessor;

            public InMemoryLogger(
                string categoryName,
                ConcurrentQueue<LogEntry> entries,
                Func<IExternalScopeProvider> scopeProviderAccessor)
            {
                _categoryName = categoryName;
                _entries = entries;
                _scopeProviderAccessor = scopeProviderAccessor;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return _scopeProviderAccessor().Push(state);
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var scopeValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                _scopeProviderAccessor().ForEachScope((scope, accumulator) => CopyScopeValues(scope, accumulator), scopeValues);

                _entries.Enqueue(new LogEntry(
                    _categoryName,
                    logLevel,
                    formatter(state, exception),
                    exception,
                    scopeValues));
            }

            private static void CopyScopeValues(object? scope, IDictionary<string, object?> accumulator)
            {
                if (scope is null)
                {
                    return;
                }

                if (scope is IEnumerable<KeyValuePair<string, object?>> scopePairs)
                {
                    foreach ((string key, object? value) in scopePairs)
                    {
                        accumulator[key] = value;
                    }

                    return;
                }

                if (scope is IEnumerable<KeyValuePair<string, object>> legacyScopePairs)
                {
                    foreach ((string key, object value) in legacyScopePairs)
                    {
                        accumulator[key] = value;
                    }

                    return;
                }

                accumulator[scope.GetType().Name] = scope;
            }
        }
    }
}
