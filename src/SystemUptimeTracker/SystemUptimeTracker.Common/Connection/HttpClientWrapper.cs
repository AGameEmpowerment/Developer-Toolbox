using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SystemUptimeTracker.Common.Authorization;
using SystemUptimeTracker.Common.Connection.Interfaces;
using SystemUptimeTracker.Common.Constants;
using System.Net;
using System.Net.Http.Headers;

namespace SystemUptimeTracker.Common.Connection;

public class HttpClientWrapper : IHttpClientWrapper
{
    private readonly IDistributedCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpClientWrapper> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public HttpClientWrapper(
        ILogger<HttpClientWrapper> logger,
        IHttpClientFactory httpClientFactory,
        IDistributedCache cache)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
    }

    public byte[] GetBytes(string resourcePath, string clientName)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(GetBytes));
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(LoggingTemplates.INFO_HTTP_RESOURCE_STANDARD_MESSAGE, resourcePath);
        }

        HttpClient httpClient = HttpClient(clientName);
        HttpResponseMessage response = httpClient.GetAsync(resourcePath).Result;
        if (response.IsSuccessStatusCode)
        {
            return response.Content.ReadAsByteArrayAsync().Result;
        }

        string msg = $"GET:{resourcePath} erred out with a result:{response.StatusCode}";
        throw new(msg);
    }

    public async Task<byte[]> GetBytesAsync(string resourcePath, string clientName)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(GetBytesAsync));
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(LoggingTemplates.INFO_HTTP_RESOURCE_STANDARD_MESSAGE, resourcePath);
        }

        HttpClient httpClient = HttpClient(clientName);
        HttpResponseMessage response = await httpClient.GetAsync(resourcePath);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsByteArrayAsync();
        }

        string msg = $"GET:{resourcePath} erred out with a result:{response.StatusCode}";
        throw new Exception(msg);
    }

    public T? GetObject<T>(string resourcePath, string clientName, AuthenticationHeaderValue? authorizationHeader = null)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(GetObject));
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(LoggingTemplates.INFO_HTTP_RESOURCE_STANDARD_MESSAGE, resourcePath);
        }

        HttpClient httpClient = HttpClient(clientName);
        using HttpRequestMessage request = CreateGetRequest(resourcePath, authorizationHeader);
        HttpResponseMessage response = httpClient.SendAsync(request).Result;
        if (response.IsSuccessStatusCode)
        {
            // Some downstream APIs still ignore the accept header and return plain text payloads.
            string stringResponse = response.Content.ReadAsStringAsync().Result;
            return JsonConvert.DeserializeObject<T>(stringResponse);
        }

        if (response.StatusCode == HttpStatusCode.NoContent ||
            response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        string msg = $"GET:{resourcePath} errored out with a result:{response.StatusCode}";
        throw new Exception(msg);
    }

    public async Task<T?> GetObjectAsync<T>(string resourcePath, string clientName, AuthenticationHeaderValue? authorizationHeader = null)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(GetObjectAsync));
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(LoggingTemplates.INFO_HTTP_RESOURCE_STANDARD_MESSAGE, resourcePath);
        }

        HttpClient httpClient = HttpClient(clientName);
        using HttpRequestMessage request = CreateGetRequest(resourcePath, authorizationHeader);
        HttpResponseMessage response = await httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            string stringResponse = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(stringResponse);
        }

        if (response.StatusCode == HttpStatusCode.NoContent ||
            response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        string msg = $"GET:{resourcePath} errored out with a result:{response.StatusCode}";
        throw new Exception(msg);
    }

    public T? GetObjectUsingAccessToken<T>(string resourcePath, string clientName, AuthenticationHeaderValue? authorizationHeader)
    {
        return GetObject<T>(resourcePath, clientName, authorizationHeader);
    }

    public string GetClientCredentialToken()
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(GetClientCredentialToken));
        }

        string? token = _cache.GetString(CacheKeyConstants.Authentication.CLIENT_CREDENTIAL_TOKEN);
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        HttpClient oauthHttpClient = HttpClient(HttpClientNames.OAUTH_CLIENT);

        string grantType = "client_credentials";

        Dictionary<string, string> form = new Dictionary<string, string>
        {
            {
                "grant_type", grantType
            },
            {
                "scope", "client_token"
            }
        };

        HttpResponseMessage tokenResponse = oauthHttpClient.PostAsync("v1/token", new FormUrlEncodedContent(form)).Result;
        string jsonContent = tokenResponse.Content.ReadAsStringAsync().Result;
        OauthToken? tok = JsonConvert.DeserializeObject<OauthToken>(jsonContent);
        if (tok is null || string.IsNullOrWhiteSpace(tok.AccessToken))
        {
            throw new InvalidOperationException("Client credentials token endpoint returned an invalid payload.");
        }

        _cache.SetString(
            CacheKeyConstants.Authentication.CLIENT_CREDENTIAL_TOKEN,
            tok.AccessToken,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(tok.ExpiresIn)
            });

        return tok.AccessToken;
    }

    public T? GetObjectUsingBearerToken<T>(string resourcePath, string clientName, string token)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(GetObjectUsingBearerToken));
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(LoggingTemplates.INFO_HTTP_RESOURCE_STANDARD_MESSAGE, resourcePath);
        }

        HttpClient httpClient = HttpClient(clientName);
        using HttpRequestMessage request = CreateGetRequest(resourcePath, new AuthenticationHeaderValue("Bearer", token));
        HttpResponseMessage response = httpClient.SendAsync(request).Result;
        if (response.IsSuccessStatusCode)
        {
            Task<string> result = response.Content.ReadAsStringAsync();
            return response.Content.ReadAsAsync<T>().Result;
        }

        if (response.StatusCode == HttpStatusCode.NoContent ||
            response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        string msg = $"GET:{resourcePath} errored out with a result: {response.StatusCode}";
        throw new Exception(msg);
    }

    public string PostData(string resourcePath, object o, string clientName)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(PostData));
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(LoggingTemplates.INFO_HTTP_RESOURCE_STANDARD_MESSAGE, resourcePath);
        }

        HttpClient httpClient = HttpClient(clientName);
        HttpResponseMessage? response = httpClient.PostAsJsonAsync(resourcePath, o).Result;
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return response.Content.ReadAsStringAsync().Result;
        }

        string msg =
            $"Post:{resourcePath} errored out with a result:{response.StatusCode} and MsgResult:{response.Content.ReadAsStringAsync().Result}";
        throw new Exception(msg);
    }

    public string PutData(string resourcePath, object o, string clientName)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(PutData));
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(LoggingTemplates.INFO_HTTP_RESOURCE_STANDARD_MESSAGE, resourcePath);
        }

        HttpClient httpClient = HttpClient(clientName);
        HttpResponseMessage? response = httpClient.PutAsJsonAsync(resourcePath, o).Result;
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return response.Content.ReadAsStringAsync().Result;
        }

        string msg;
        try
        {
            msg = response.Content.ReadAsStringAsync().Result;
        }
        catch (Exception ex)
        {
            msg = $"Unable to read the response message {ex.Message}";
        }

        throw new Exception($"Put:{resourcePath} errored out with a code of {response.StatusCode} and a message of:\n {msg}");
    }

    public T Post<Tk, T>(string resourcePath, Tk data, string clientName)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(Post));
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(LoggingTemplates.INFO_HTTP_RESOURCE_STANDARD_MESSAGE, resourcePath);
        }

        HttpClient httpClient = HttpClient(clientName);
        HttpResponseMessage? response = httpClient.PostAsJsonAsync<Tk>(resourcePath, data).Result;
        if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created)
        {
            return response.Content.ReadAsAsync<T>().Result;
        }

        string msg =
            $"Post:{resourcePath} errored out with a result:{response.StatusCode} and MsgResult:{response.Content.ReadAsStringAsync().Result}";
        throw new Exception(msg);
    }

    public T Put<Tk, T>(string resourcePath, Tk data, string clientName)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(Put));
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(LoggingTemplates.INFO_HTTP_RESOURCE_STANDARD_MESSAGE, resourcePath);
        }

        HttpClient httpClient = HttpClient(clientName);
        HttpResponseMessage? response = httpClient.PutAsJsonAsync<Tk>(resourcePath, data).Result;
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return response.Content.ReadAsAsync<T>().Result;
        }

        string msg =
            $"Put:{resourcePath} errored out with a result:{response.StatusCode} and MsgResult:{response.Content.ReadAsStringAsync().Result}";
        throw new Exception(msg);
    }

    public string Delete(string resourcePath, string clientName)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(Delete));
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(LoggingTemplates.INFO_HTTP_RESOURCE_STANDARD_MESSAGE, resourcePath);
        }

        HttpClient httpClient = HttpClient(clientName);
        HttpResponseMessage response = httpClient.DeleteAsync(resourcePath).Result;
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return response.Content.ReadAsStringAsync().Result;
        }

        string msg =
            $"Delete:{resourcePath} errored out with a result:{response.StatusCode} and MsgResult:{response.Content.ReadAsStringAsync().Result}";

        throw new Exception(msg);
    }

    private HttpClient HttpClient(string clientName)
    {
        HttpClient httpClient = _httpClientFactory.CreateClient(clientName);
        return httpClient;
    }

    // Build outbound GET requests from explicit caller input so request-state discovery stays outside this wrapper.
    private static HttpRequestMessage CreateGetRequest(string resourcePath, AuthenticationHeaderValue? authorizationHeader)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, resourcePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (authorizationHeader is not null)
        {
            request.Headers.Authorization = authorizationHeader;
        }

        return request;
    }
}
