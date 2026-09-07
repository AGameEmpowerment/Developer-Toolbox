using System.Net.Http.Headers;

namespace SystemUptimeTracker.Common.Connection.Interfaces;

public interface IHttpClientWrapper
{
    Task<byte[]> GetBytesAsync(
        string resourcePath,
        string clientName,
        CancellationToken cancellationToken = default);

    Task<T?> GetObjectAsync<T>(
        string resourcePath,
        string clientName,
        AuthenticationHeaderValue? authorizationHeader = null,
        CancellationToken cancellationToken = default);
}
