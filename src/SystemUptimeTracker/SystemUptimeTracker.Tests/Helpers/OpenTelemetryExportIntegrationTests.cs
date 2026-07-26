using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SystemUptimeTracker.Common.Helpers.Data;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;

namespace SystemUptimeTracker.Tests.Helpers;

[TestFixture(Category = "Integration")]
public sealed class OpenTelemetryExportIntegrationTests
{
    private static readonly ActivitySource TestActivitySource = new("SystemUptimeTracker.Tests.OpenTelemetry");
    private static readonly Meter TestMeter = new("SystemUptimeTracker.Tests.OpenTelemetry");
    private static readonly Counter<long> TestCounter = TestMeter.CreateCounter<long>("systemuptimetracker.test.requests");

    [Test]
    public async Task OpenTelemetry_WhenOtlpExporterConfigured_PostsLogsTracesAndMetricsToConfiguredSignalEndpoints()
    {
        TelemetryCaptureHost? captureHost = null;
        WebApplication? app = null;

        try
        {
            captureHost = await TelemetryCaptureHost.StartAsync();
            app = await CreateApplicationAsync(captureHost.BaseAddress);

            HttpClient client = app.GetTestClient();
            HttpResponseMessage response = await client.GetAsync("/test/ping");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            LoggerProvider loggerProvider = app.Services.GetRequiredService<LoggerProvider>();
            TracerProvider tracerProvider = app.Services.GetRequiredService<TracerProvider>();
            MeterProvider meterProvider = app.Services.GetRequiredService<MeterProvider>();

            Assert.Multiple(() =>
            {
                Assert.That(loggerProvider.ForceFlush(), Is.True, "Expected logs to flush to the configured OTLP endpoint.");
                Assert.That(tracerProvider.ForceFlush(), Is.True, "Expected traces to flush to the configured OTLP endpoint.");
                Assert.That(meterProvider.ForceFlush(), Is.True, "Expected metrics to flush to the configured OTLP endpoint.");
            });

            await app.DisposeAsync();
            app = null;

            await captureHost.WaitForSignalsAsync("logs", "traces", "metrics");

            IReadOnlyCollection<TelemetryCaptureHost.CapturedRequest> requests = captureHost.Requests.ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(requests.Any(request => request.SignalName == "logs"), Is.True, "Expected at least one OTLP log export request.");
                Assert.That(requests.Any(request => request.SignalName == "traces"), Is.True, "Expected at least one OTLP trace export request.");
                Assert.That(requests.Any(request => request.SignalName == "metrics"), Is.True, "Expected at least one OTLP metric export request.");
                Assert.That(requests.All(request => request.ContentLength > 0), Is.True, "Expected each captured OTLP request to include a payload body.");
            });
        }
        finally
        {
            if (app is not null)
            {
                await app.DisposeAsync();
            }

            if (captureHost is not null)
            {
                await captureHost.DisposeAsync();
            }
        }
    }

    private static async Task<WebApplication> CreateApplicationAsync(Uri otlpBaseAddress)
    {
        Uri logsEndpoint = LoggerSupport.BuildOtlpSignalEndpoint(otlpBaseAddress.ToString(), "logs")
            ?? throw new InvalidOperationException("Expected a logs OTLP endpoint.");
        Uri tracesEndpoint = LoggerSupport.BuildOtlpSignalEndpoint(otlpBaseAddress.ToString(), "traces")
            ?? throw new InvalidOperationException("Expected a traces OTLP endpoint.");
        Uri metricsEndpoint = LoggerSupport.BuildOtlpSignalEndpoint(otlpBaseAddress.ToString(), "metrics")
            ?? throw new InvalidOperationException("Expected a metrics OTLP endpoint.");

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(CreateResourceBuilder());
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.AddOtlpExporter(otlpOptions => ConfigureExporter(otlpOptions, logsEndpoint));
        });
        builder.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            options.MinLevel = LogLevel.Information;
            options.Rules.Clear();
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("SystemUptimeTracker.Tests.OpenTelemetry", autoGenerateServiceInstanceId: false))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(TestActivitySource.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddOtlpExporter(otlpOptions => ConfigureExporter(otlpOptions, tracesEndpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(TestMeter.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddOtlpExporter((otlpOptions, _) => ConfigureExporter(otlpOptions, metricsEndpoint));
            });

        WebApplication app = builder.Build();
        app.MapGet("/test/ping", (ILogger<OpenTelemetryExportIntegrationTests> logger) =>
        {
            using Activity? activity = TestActivitySource.StartActivity("telemetry-export-test");
            TestCounter.Add(1, new KeyValuePair<string, object?>("route", "/test/ping"));
            logger.LogInformation("Telemetry export regression probe executed.");
            return Results.Ok(new { status = "ok" });
        });

        await app.StartAsync();
        return app;
    }

    private static ResourceBuilder CreateResourceBuilder()
    {
        return ResourceBuilder.CreateDefault()
            .AddService("SystemUptimeTracker.Tests.OpenTelemetry", autoGenerateServiceInstanceId: false);
    }

    private static void ConfigureExporter(OtlpExporterOptions options, Uri endpoint)
    {
        options.Endpoint = endpoint;
        options.Protocol = OtlpExportProtocol.HttpProtobuf;
    }

    private sealed class TelemetryCaptureHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<CapturedRequest> _requests;

        private TelemetryCaptureHost(WebApplication app, ConcurrentQueue<CapturedRequest> requests, Uri baseAddress)
        {
            _app = app;
            _requests = requests;
            BaseAddress = baseAddress;
        }

        public Uri BaseAddress { get; }

        public ConcurrentQueue<CapturedRequest> Requests => _requests;

        public static async Task<TelemetryCaptureHost> StartAsync()
        {
            var requests = new ConcurrentQueue<CapturedRequest>();

            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseUrls("http://127.0.0.1:0");

            WebApplication app = builder.Build();
            app.MapPost("/v1/{signalName}", async (string signalName, HttpRequest request) =>
            {
                using var buffer = new MemoryStream();
                await request.Body.CopyToAsync(buffer);
                requests.Enqueue(new CapturedRequest(signalName, request.ContentType, buffer.Length));
                return Results.Ok();
            });

            await app.StartAsync();

            string? baseAddress = app.Urls.SingleOrDefault();

            if (string.IsNullOrWhiteSpace(baseAddress))
            {
                throw new InvalidOperationException("Expected the telemetry capture host to expose a bound address after startup.");
            }

            return new TelemetryCaptureHost(app, requests, new Uri(baseAddress, UriKind.Absolute));
        }

        public async Task WaitForSignalsAsync(params string[] expectedSignals)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);

            while (DateTimeOffset.UtcNow < deadline)
            {
                HashSet<string> observedSignals = _requests
                    .Select(request => request.SignalName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (expectedSignals.All(signal => observedSignals.Contains(signal)))
                {
                    return;
                }

                await Task.Delay(100);
            }

            string observed = string.Join(", ", _requests.Select(request => request.SignalName));
            throw new AssertionException(
                $"Timed out waiting for OTLP requests. Expected={string.Join(", ", expectedSignals)}; Observed={observed}.");
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
        }

        public sealed record CapturedRequest(string SignalName, string? ContentType, long ContentLength);
    }
}
