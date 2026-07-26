namespace SystemUptimeTracker.Common.Helpers.Extensions;

public static class StringExtensions
{
    /// <summary>
    /// Replaces double slashes in a URL with a single slash.
    /// </summary>
    /// <param name="url">The URL string to be scrubbed.</param>
    /// <returns>The scrubbed URL string with double slashes replaced by a single slash.</returns>
    public static string? ScrubUrlRoute(this string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (url.StartsWith("http://") || url.StartsWith("https://"))
        {
            string protocol = url.StartsWith("http://") ? "http://" : "https://";
            string restOfUrl = url.Substring(protocol.Length);
            return protocol + restOfUrl.Replace("//", "/");
        }

        return url.Replace("//", "/");
    }

    /// <summary>
    /// Masks a string by replacing characters after the first four with asterisks.
    /// </summary>
    /// <param name="s">The string to be masked.</param>
    /// <returns>
    /// A masked string where characters after the first four are replaced with asterisks.
    /// If the string is null, empty, or consists only of white-space characters, the original string is returned.
    /// If the string length is 4 or less, returns "****".
    /// </returns>
    public static string? Mask(this string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        if (s.Length <= 4)
        {
            return "****";
        }

        return $"{s.Substring(0, 4)}********";
    }
}
