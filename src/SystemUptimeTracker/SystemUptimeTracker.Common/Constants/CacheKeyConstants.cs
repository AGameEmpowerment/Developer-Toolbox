namespace SystemUptimeTracker.Common.Constants;

/// <summary>
/// Centralizes distributed cache keys so all server-side cache consumers use the same shapes.
/// </summary>
public static class CacheKeyConstants
{
    /// <summary>
    /// Cache keys for lookup data.
    /// </summary>
    public static class Lookups
    {
        /// <summary>
        /// Cache key for the countries lookup payload.
        /// </summary>
        public const string COUNTRIES = "Lookups:Countries";
    }

    /// <summary>
    /// Cache keys for authorization data.
    /// </summary>
    public static class UserRights
    {
        /// <summary>
        /// Builds the cache key for a user's permission payload.
        /// </summary>
        /// <param name="accountId">The account identifier. Must be non-empty.</param>
        /// <returns>A normalized cache key for the account.</returns>
        public static string GetUserRightsKey(string accountId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

            var normalizedAccountId = accountId.Trim().ToLowerInvariant();
            return $"UserRights:Account:{normalizedAccountId}";
        }
    }

    /// <summary>
    /// Cache keys for authentication infrastructure data.
    /// </summary>
    public static class Authentication
    {
        /// <summary>
        /// Cache key for the client-credentials bearer token.
        /// </summary>
        public const string CLIENT_CREDENTIAL_TOKEN = "Authentication:ClientCredentialToken";
    }
}
