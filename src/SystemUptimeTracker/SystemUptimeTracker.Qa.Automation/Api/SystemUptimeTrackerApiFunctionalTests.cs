using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SystemUptimeTracker.Qa.Automation.Contracts;
using SystemUptimeTracker.Qa.Automation.Services;
using SystemUptimeTracker.Qa.Automation.Support;
using SystemUptimeTracker.Qa.Automation.TestBases;

namespace SystemUptimeTracker.Qa.Automation.Api;

[TestFixture(Category = "Automation"), Category("Integration"), Category("Api")]
public sealed class SystemUptimeTrackerApiFunctionalTests : SystemUptimeTrackerApiTestBase
{
    [Test, Category("Smoke")]
    public async Task HealthEndpoint_ReturnsHealthyStatus()
    {
        Logger.LogInformation("Validating health endpoint.");
        ApiResponse<HealthCheckResponse> response = await SystemUptimeTrackerApi.GetHealthResponseAsync();
        HealthCheckResponse health = response.Payload;

        Assert.Multiple(() =>
        {
            Assert.That(health.Status, Is.EqualTo("Healthy"));
            Assert.That(health.Entries, Is.Not.Empty);
            Assert.That(health.TotalDuration, Is.Not.Empty);
            Assert.That(response.TraceId, Is.Not.Null.And.Not.Empty);
        });

        Logger.LogInformation("Health endpoint reported status {HealthStatus} with trace id {TraceId}.", health.Status, response.TraceId);
    }
    [Test, Category("Functional")]
    public async Task ServerSettingsEndpoint_ReflectsLocalDevelopmentFlags()
    {
        Logger.LogInformation("Validating server settings endpoint shape.");
        JsonDocument? settings = null;

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ITestIdentityAccountProvisioningService provisioningService = scope.ServiceProvider.GetRequiredService<ITestIdentityAccountProvisioningService>();
        TestIdentityAccountProvisioningResult provisioningResult = await provisioningService.EnsureIndividualAccountReadyAsync();
        var credentials = new LoginCredentials
        {
            Username = provisioningResult.Email,
            Password = provisioningResult.Password
        };

        try
        {
            settings = await SystemUptimeTrackerApi.GetServerSettingsAsync(credentials);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            Logger.LogInformation("Server settings endpoint is disabled in the current environment; skipping assertions.");
            Assert.Ignore("The server settings endpoint is disabled in this environment.");
        }

        Assert.That(settings, Is.Not.Null);

        using (settings)
        {
            Assert.Multiple(() =>
            {
                JsonElement featureManagement = settings.RootElement.GetProperty("featureManagement");
                Assert.That(featureManagement.ValueKind, Is.EqualTo(JsonValueKind.Object));

                JsonElement aspireEnabled = featureManagement.GetProperty("aspireEnabled");
                Assert.That(aspireEnabled.GetBoolean(), Is.True);

                JsonElement openApiEnabled = featureManagement.GetProperty("openApiEnabled");
                Assert.That(openApiEnabled.ValueKind, Is.EqualTo(JsonValueKind.True).Or.EqualTo(JsonValueKind.False));
            });

            Logger.LogInformation("Server settings endpoint returned the expected configuration shape.");
        }
    }

    [Test, Category("Functional")]
    public async Task StatusJsonEndpoint_ReturnsSystemUptimeTrackerApiMetadata()
    {
        Logger.LogInformation("Validating server status endpoint.");
        using JsonDocument status = await SystemUptimeTrackerApi.GetServerStatusAsync();

        Assert.Multiple(() =>
        {
            Assert.That(status.RootElement.GetProperty("Title").GetString(), Is.EqualTo("System Uptime Tracker API"));
            Assert.That(status.RootElement.GetProperty("ProjectInfoCollection").GetArrayLength(), Is.GreaterThan(0));
        });

        Logger.LogInformation("Server status endpoint returned metadata for the SystemUptimeTracker API.");
    }

    [Test, Category("Functional")]
    public async Task OperationsMetadataEndpoint_ReturnsSafeVersionAndStartupMetadata()
    {
        Logger.LogInformation("Validating operations metadata endpoint.");
        OperationsMetadataResponse metadata = await SystemUptimeTrackerApi.GetOperationsMetadataAsync();

        Assert.Multiple(() =>
        {
            Assert.That(metadata.ApplicationName, Is.EqualTo("SystemUptimeTracker.Api"));
            Assert.That(metadata.ApplicationVersion, Is.Not.Null.And.Not.Empty);
            Assert.That(metadata.BuildVersion, Is.Not.Null.And.Not.Empty);
            Assert.That(metadata.Environment, Is.Not.Null.And.Not.Empty);
            Assert.That(metadata.StartedAtUtc, Is.Not.EqualTo(default(DateTimeOffset)));
        });

        Logger.LogInformation(
            "Operations metadata endpoint returned version {ApplicationVersion} and startup time {StartedAtUtc}.",
            metadata.ApplicationVersion,
            metadata.StartedAtUtc);
    }

    [Test, Category("Functional")]
    public async Task ControlledFailureEndpoint_ReturnsScrubbedProblemDetailsWithTraceCorrelation()
    {
        Logger.LogInformation("Validating controlled failure endpoint.");
        ApiProblemResponse response = await SystemUptimeTrackerApi.TriggerControlledFailureAsync();

        using (response.Problem)
        {
            JsonElement root = response.Problem.RootElement;

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
                Assert.That(response.TraceId, Is.Not.Null.And.Not.Empty);
                Assert.That(root.GetProperty("title").GetString(), Is.EqualTo("An unexpected error occurred."));
                Assert.That(root.GetProperty("detail").GetString(), Does.Contain(response.TraceId));
                Assert.That(root.GetProperty("detail").GetString(), Does.Not.Contain("SELECT * FROM dbo.Users"));
                Assert.That(root.GetProperty("detail").GetString(), Does.Not.Contain("C:\\systemuptimetracker\\assets\\secret"));
                Assert.That(root.GetProperty("traceId").GetString(), Is.EqualTo(response.TraceId));
            });
        }

        Logger.LogInformation("Controlled failure endpoint returned a scrubbed problem payload with trace id {TraceId}.", response.TraceId);
    }
}
