using System.Text.Json;
using SystemUptimeTracker.Qa.Automation.Contracts;
using SystemUptimeTracker.Qa.Automation.Support;

namespace SystemUptimeTracker.Qa.Automation.Services;

public interface ISystemUptimeTrackerApiClient
{
    Task<HealthCheckResponse> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<ApiResponse<HealthCheckResponse>> GetHealthResponseAsync(CancellationToken cancellationToken = default);

    Task<bool> GetFeatureFlagAsync(string flag, bool useGlobalScope = false, CancellationToken cancellationToken = default);

    Task<OperationsMetadataResponse> GetOperationsMetadataAsync(CancellationToken cancellationToken = default);

    Task<ApiProblemResponse> TriggerControlledFailureAsync(CancellationToken cancellationToken = default);

    Task<JsonDocument> GetServerEnvironmentAsync(CancellationToken cancellationToken = default);

    Task<JsonDocument> GetServerSettingsAsync(CancellationToken cancellationToken = default);

    Task<string> RequestLocalIdentityAccessTokenAsync(LoginCredentials credentials, CancellationToken cancellationToken = default);

    Task<JsonDocument> GetServerSettingsAsync(LoginCredentials credentials, CancellationToken cancellationToken = default);

    Task<JsonDocument> GetServerStatusAsync(CancellationToken cancellationToken = default);
}
