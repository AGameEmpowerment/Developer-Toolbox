using System.Net.Http.Headers;

namespace SystemUptimeTracker.Common.Connection.Interfaces;

public interface IHttpClientWrapper
{
    string GetClientCredentialToken();
    T? GetObjectUsingBearerToken<T>(string resourcePath, string clientName, string token);
    T? GetObjectUsingAccessToken<T>(string resourcePath, string clientName, AuthenticationHeaderValue? authorizationHeader);
    T? GetObject<T>(string resourcePath, string clientName, AuthenticationHeaderValue? authorizationHeader = null);
    Task<T?> GetObjectAsync<T>(string resourcePath, string clientName, AuthenticationHeaderValue? authorizationHeader = null);
    byte[] GetBytes(string resourcePath, string clientName);
    Task<byte[]> GetBytesAsync(string resourcePath, string clientName);
    string PostData(string resourcePath, object o, string clientName);
    string PutData(string resourcePath, object o, string clientName);
    string Delete(string resourcePath, string clientName);
    T Post<Tk, T>(string resourcePath, Tk data, string clientName);
    T Put<Tk, T>(string resourcePath, Tk data, string clientName);
}
