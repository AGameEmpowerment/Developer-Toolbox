using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http.Headers;
using SystemUptimeTracker.Common.Connection.Interfaces;
using SystemUptimeTracker.Common.Constants;

namespace SystemUptimeTracker.Common.Connection;

public class HttpClientWrapper : IHttpClientWrapper
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpClientWrapper> _logger;

    public HttpClientWrapper(
        ILogger<HttpClientWrapper> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<byte[]> GetBytesAsync(
        string resourcePath,
        string clientName,
        CancellationToken cancellationToken = default)
    {
        LogRequest(resourcePath, nameof(GetBytesAsync));

        HttpClient httpClient = _httpClientFactory.CreateClient(clientName);
        using HttpResponseMessage response = await httpClient
            .GetAsync(resourcePath, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateRequestException(HttpMethod.Get, resourcePath, response.StatusCode);
        }

        return await response.Content
            .ReadAsByteArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<T?> GetObjectAsync<T>(
        string resourcePath,
        string clientName,
        AuthenticationHeaderValue? authorizationHeader = null,
        CancellationToken cancellationToken = default)
    {
        LogRequest(resourcePath, nameof(GetObjectAsync));

        HttpClient httpClient = _httpClientFactory.CreateClient(clientName);
        using HttpRequestMessage request = CreateGetRequest(resourcePath, authorizationHeader);
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw CreateRequestException(HttpMethod.Get, resourcePath, response.StatusCode);
        }

        string responseContent = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        return JsonConvert.DeserializeObject<T>(responseContent);
    }

    private void LogRequest(string resourcePath, string methodName)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, methodName);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(LoggingTemplates.INFO_HTTP_RESOURCE_STANDARD_MESSAGE, resourcePath);
        }
    }

    private static HttpRequestException CreateRequestException(
        HttpMethod method,
        string resourcePath,
        HttpStatusCode statusCode) =>
        new(
            $"{method.Method} request to '{resourcePath}' failed with status code {(int)statusCode} ({statusCode}).",
            inner: null,
            statusCode);

    private static HttpRequestMessage CreateGetRequest(
        string resourcePath,
        AuthenticationHeaderValue? authorizationHeader)
    {
        HttpRequestMessage request = new(HttpMethod.Get, resourcePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (authorizationHeader is not null)
        {
            request.Headers.Authorization = authorizationHeader;
        }

        return request;
    }
}
