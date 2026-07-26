using Microsoft.Extensions.Compliance.Redaction;

namespace SystemUptimeTracker.Common.Helpers.Filter;

/// <summary>
/// Provides a custom redaction capability that replaces all characters with asterisks.
/// </summary>
public class StarRedactor : Redactor
{
    /// <summary>
    /// Redacts the input source by replacing all characters with asterisks.
    /// </summary>
    /// <param name="source">The input characters to be redacted.</param>
    /// <param name="destination">The destination span where the redacted characters will be written.</param>
    /// <returns>The length of the redacted content.</returns>
    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        destination.Fill('*');
        return destination.Length;
    }

    /// <summary>
    /// Gets the length of the redacted content, which is the same as the input length.
    /// </summary>
    /// <param name="input">The input characters to be redacted.</param>
    /// <returns>The length of the redacted content.</returns>
    public override int GetRedactedLength(ReadOnlySpan<char> input)
    {
        return input.Length;
    }
}