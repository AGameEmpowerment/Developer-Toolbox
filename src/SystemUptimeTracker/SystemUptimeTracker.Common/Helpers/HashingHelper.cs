using System.Security.Cryptography;
using System.Text;

namespace SystemUptimeTracker.Common.Helpers;

/// <summary>
/// Utility class for generating cryptographic hashes of message content for audit purposes.
/// </summary>
public static class HashingHelper
{
    /// <summary>
    /// Generates a SHA-256 hash of the normalized text content.
    /// Normalizes line endings and trims whitespace for consistent hashing.
    /// </summary>
    /// <param name="text">The text to hash</param>
    /// <returns>SHA-256 hash as a hexadecimal string (64 characters)</returns>
    public static string ComputeSha256Hash(string? text)
    {
        // Normalize the text for consistent hashing
        var normalizedText = NormalizeTextForHashing(text ?? string.Empty);
        
        // Convert the string to bytes
        var textBytes = Encoding.UTF8.GetBytes(normalizedText);
        
        // Compute the hash
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(textBytes);
        
        // Convert to hexadecimal string
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes text for consistent hashing by trimming whitespace and normalizing line endings.
    /// </summary>
    /// <param name="text">The text to normalize</param>
    /// <returns>Normalized text suitable for hashing</returns>
    private static string NormalizeTextForHashing(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Trim()
            .Replace("\r\n", "\n") // Normalize Windows line endings to Unix
            .Replace("\r", "\n");   // Normalize old Mac line endings to Unix
    }

    /// <summary>
    /// Validates that a given text matches the provided SHA-256 hash.
    /// Useful for integrity verification during audit operations.
    /// </summary>
    /// <param name="text">The text to verify</param>
    /// <param name="expectedHash">The expected SHA-256 hash</param>
    /// <returns>True if the text matches the hash, false otherwise</returns>
    public static bool VerifyTextHash(string? text, string expectedHash)
    {
        if (string.IsNullOrEmpty(expectedHash))
        {
            return false;
        }

        var actualHash = ComputeSha256Hash(text);
        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
