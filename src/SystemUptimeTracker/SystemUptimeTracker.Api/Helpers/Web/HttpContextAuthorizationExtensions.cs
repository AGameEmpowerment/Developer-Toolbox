using System.Net.Http.Headers;

namespace SystemUptimeTracker.Api.Helpers.Web;

/// <summary>
/// Helpers for resolving the inbound authorization header inside the request layer.
/// </summary>
public static class HttpContextAuthorizationExtensions
{
    /// <summary>
    /// Parses the current request authorization header when one is present.
    /// </summary>
    /// <param name="context">The active HTTP context.</param>
    /// <returns>The parsed authorization header when present and valid; otherwise <see langword="null" />.</returns>
    public static AuthenticationHeaderValue? GetClientAuthorizationHeader(this HttpContext? context)
    {
        if (context?.Request?.Headers is null)
        {
            return null;
        }

        var rawAuthorizationHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(rawAuthorizationHeader))
        {
            return null;
        }

        return AuthenticationHeaderValue.TryParse(rawAuthorizationHeader, out var parsedHeader)
            ? parsedHeader
            : null;
    }
}
