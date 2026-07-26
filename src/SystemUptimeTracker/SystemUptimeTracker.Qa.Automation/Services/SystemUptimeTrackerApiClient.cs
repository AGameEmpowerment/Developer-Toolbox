using System.Text.Json;
using SystemUptimeTracker.Qa.Automation.Support;
using Microsoft.Extensions.Logging;
using SystemUptimeTracker.Qa.Automation.Contracts;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace SystemUptimeTracker.Qa.Automation.Services;

public sealed class SystemUptimeTrackerApiClient : ISystemUptimeTrackerApiClient
{
    private const string TRACE_ID_HEADER_NAME = "X-Trace-Id";
    private const string ANTIFORGERY_RESOURCE = "api/auth/antiforgery-token";
    private const string LOCAL_IDENTITY_LOGIN_RESOURCE = "api/identity/login?useCookies=false&useSessionCookies=false";

    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IApiClientFactory _apiClientFactory;
    private readonly ILogger<SystemUptimeTrackerApiClient> _logger;

    public SystemUptimeTrackerApiClient(IApiClientFactory apiClientFactory, ILogger<SystemUptimeTrackerApiClient> logger)
    {
        _apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<HealthCheckResponse> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Requesting health check payload.");
        return GetJsonAsync<HealthCheckResponse>("_health", cancellationToken);
    }

    public Task<ApiResponse<HealthCheckResponse>> GetHealthResponseAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Requesting health check payload with response trace metadata.");
        return GetApiResponseAsync<HealthCheckResponse>("_health", cancellationToken);
    }

    public Task<bool> GetFeatureFlagAsync(string flag, bool useGlobalScope = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flag);
        string resource = $"api/featureflags/feature?flag={Uri.EscapeDataString(flag)}&global={useGlobalScope.ToString().ToLowerInvariant()}";
        _logger.LogInformation("Requesting feature flag {Flag} with global scope {UseGlobalScope}.", flag, useGlobalScope);
        return GetJsonAsync<bool>(resource, cancellationToken);
    }

    public Task<OperationsMetadataResponse> GetOperationsMetadataAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Requesting operations metadata payload.");
        return GetJsonAsync<OperationsMetadataResponse>("api/operations/metadata", cancellationToken);
    }

    public async Task<ApiProblemResponse> TriggerControlledFailureAsync(CancellationToken cancellationToken = default)
    {
        using HttpClient client = CreateClient();
        const string RESOURCE = "api/operations/validate-error-shape";

        _logger.LogInformation("Requesting controlled failure payload from {Resource}.", RESOURCE);

        using HttpResponseMessage response = await client.GetAsync(RESOURCE, cancellationToken);
        string requestUri = response.RequestMessage?.RequestUri?.ToString() ?? RESOURCE;
        string traceId = GetTraceId(response);

        _logger.LogInformation(
            "Received {StatusCode} from {RequestUri} with trace id {TraceId}.",
            (int)response.StatusCode,
            requestUri,
            traceId);

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        JsonDocument problem = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);

        return new ApiProblemResponse
        {
            StatusCode = response.StatusCode,
            TraceId = traceId,
            Problem = problem
        };
    }

    public Task<JsonDocument> GetServerEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Requesting server environment payload.");
        return GetJsonDocumentAsync("api/info/environment", cancellationToken);
    }

    public Task<JsonDocument> GetServerSettingsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Requesting server settings payload.");
        return GetJsonDocumentAsync("api/info/settings", cancellationToken);
    }

    public async Task<string> RequestLocalIdentityAccessTokenAsync(LoginCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.Password);

        _logger.LogInformation("Requesting a local identity access token for {Username}.", credentials.Username);

        using HttpClient client = CreateClient();
        return await SignInLocalIdentityAsync(client, credentials, cancellationToken);
    }

    public async Task<JsonDocument> GetServerSettingsAsync(LoginCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.Password);

        _logger.LogInformation("Requesting authenticated server settings payload for {Username}.", credentials.Username);

        using HttpClient client = CreateClient();
        string accessToken = await SignInLocalIdentityAsync(client, credentials, cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await GetJsonDocumentAsync(client, "api/info/settings", cancellationToken);
    }

    public Task<JsonDocument> GetServerStatusAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Requesting server status payload.");
        return GetJsonDocumentAsync("api/info/statusjson", cancellationToken);
    }

    private HttpClient CreateClient()
    {
        return _apiClientFactory.InitHttpClient("application/json");
    }

    private async Task<T> GetJsonAsync<T>(string resource, CancellationToken cancellationToken)
    {
        using HttpClient client = CreateClient();
        _logger.LogDebug("Sending GET request to resource {Resource}.", resource);
        using HttpResponseMessage response = await client.GetAsync(resource, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<ApiResponse<T>> GetApiResponseAsync<T>(string resource, CancellationToken cancellationToken)
    {
        using HttpClient client = CreateClient();
        _logger.LogDebug("Sending GET request to resource {Resource}.", resource);
        using HttpResponseMessage response = await client.GetAsync(resource, cancellationToken);

        T payload = await ReadJsonAsync<T>(response, cancellationToken);
        return new ApiResponse<T>
        {
            Payload = payload,
            TraceId = GetTraceId(response)
        };
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(string resource, CancellationToken cancellationToken)
    {
        using HttpClient client = CreateClient();
        return await GetJsonDocumentAsync(client, resource, cancellationToken);
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(HttpClient client, string resource, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Sending GET request to resource {Resource}.", resource);
        using HttpResponseMessage response = await client.GetAsync(resource, cancellationToken);

        string requestUri = response.RequestMessage?.RequestUri?.ToString() ?? resource;
        _logger.LogInformation("Received {StatusCode} from {RequestUri}.", (int)response.StatusCode, requestUri);

        response.EnsureSuccessStatusCode();

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        _logger.LogInformation("Parsed JSON document response from {RequestUri}.", requestUri);
        return document;
    }

    private async Task<string> SignInLocalIdentityAsync(HttpClient client, LoginCredentials credentials, CancellationToken cancellationToken)
    {
        using StringContent content = new(
            JsonSerializer.Serialize(new LocalIdentityLoginRequest(credentials.Username, credentials.Password), _serializerOptions),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await client.PostAsync(
            LOCAL_IDENTITY_LOGIN_RESOURCE,
            content,
            cancellationToken);

        string requestUri = response.RequestMessage?.RequestUri?.ToString() ?? LOCAL_IDENTITY_LOGIN_RESOURCE;
        _logger.LogInformation("Received {StatusCode} from {RequestUri} during QA local identity sign-in.", (int)response.StatusCode, requestUri);

        if (!response.IsSuccessStatusCode)
        {
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            string traceId = GetTraceId(response);
            string setupStatus = await TryGetIdentitySetupStatusAsync(client, cancellationToken);

            throw new InvalidOperationException(
                $"The QA local identity sign-in request to {requestUri} returned {(int)response.StatusCode} ({response.StatusCode}). TraceId='{traceId}'. Body='{responseBody}'. IdentitySetupStatus='{setupStatus}'.");
        }

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        LocalIdentityLoginResponse? payload = await JsonSerializer.DeserializeAsync<LocalIdentityLoginResponse>(
            contentStream,
            _serializerOptions,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(payload?.AccessToken))
        {
            throw new InvalidOperationException($"No access token was returned from {requestUri}.");
        }

        return payload.AccessToken;
    }

    private static async Task<string> TryGetIdentitySetupStatusAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await client.GetAsync("api/identity/setup-status", cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return $"Status={(int)response.StatusCode} ({response.StatusCode}); Body={body}";
        }
        catch (Exception exception)
        {
            return $"Unavailable: {exception.GetType().Name}: {exception.Message}";
        }
    }

    private async Task<ApiTextResponse> SendAuthenticatedAsync(
        HttpMethod method,
        string resource,
        LoginCredentials credentials,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.Password);

        using HttpClient client = CreateClient();
        string accessToken = await SignInLocalIdentityAsync(client, credentials, cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var request = new HttpRequestMessage(method, resource)
        {
            Content = content
        };

        if (method != HttpMethod.Get)
        {
            AntiforgeryTokenResponse antiforgeryToken = await GetAntiforgeryTokenAsync(client, cancellationToken);
            request.Headers.Add(antiforgeryToken.HeaderName, antiforgeryToken.RequestToken);
        }

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        string requestUri = response.RequestMessage?.RequestUri?.ToString() ?? resource;
        string traceId = GetTraceId(response);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        _logger.LogInformation(
            "Received {StatusCode} from {RequestUri} with trace id {TraceId} for authenticated {Method} request.",
            (int)response.StatusCode,
            requestUri,
            traceId,
            method.Method);

        return new ApiTextResponse
        {
            StatusCode = response.StatusCode,
            TraceId = traceId,
            Body = body
        };
    }

    private async Task<AntiforgeryTokenResponse> GetAntiforgeryTokenAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(ANTIFORGERY_RESOURCE, cancellationToken);
        string requestUri = response.RequestMessage?.RequestUri?.ToString() ?? ANTIFORGERY_RESOURCE;
        _logger.LogInformation("Received {StatusCode} from {RequestUri} while requesting QA antiforgery token.", (int)response.StatusCode, requestUri);

        response.EnsureSuccessStatusCode();

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        AntiforgeryTokenResponse? token = await JsonSerializer.DeserializeAsync<AntiforgeryTokenResponse>(
            contentStream,
            _serializerOptions,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(token?.RequestToken) || string.IsNullOrWhiteSpace(token.HeaderName))
        {
            throw new InvalidOperationException($"The antiforgery token response from {requestUri} was incomplete.");
        }

        return token;
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string requestUri = response.RequestMessage?.RequestUri?.ToString() ?? "unknown";
        _logger.LogInformation("Received {StatusCode} from {RequestUri}.", (int)response.StatusCode, requestUri);

        response.EnsureSuccessStatusCode();

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        T? result = await JsonSerializer.DeserializeAsync<T>(contentStream, _serializerOptions, cancellationToken: cancellationToken);
        _logger.LogInformation("Deserialized {PayloadType} response from {RequestUri}.", typeof(T).Name, requestUri);

        return result ?? throw new InvalidOperationException($"No JSON payload was returned from {requestUri}.");
    }

    private static string GetTraceId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues(TRACE_ID_HEADER_NAME, out IEnumerable<string>? values))
        {
            return values.FirstOrDefault() ?? string.Empty;
        }

        return string.Empty;
    }

    private sealed record LocalIdentityLoginRequest(string Email, string Password);

    private sealed record LocalIdentityLoginResponse(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType);

    private sealed record AntiforgeryTokenResponse(string RequestToken, string HeaderName);
}
