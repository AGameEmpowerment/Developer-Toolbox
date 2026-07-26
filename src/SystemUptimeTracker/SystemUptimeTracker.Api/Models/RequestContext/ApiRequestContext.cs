using System.Net.Http.Headers;

namespace SystemUptimeTracker.Api.Models.RequestContext;

/// <summary>
/// Materializes the request-scoped identity and impersonation contract once per controller instance.
/// </summary>
public sealed class ApiRequestContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiRequestContext"/> class.
    /// </summary>
    /// <param name="accountId">The signed-in account identifier.</param>
    /// <param name="authorizedImpersonatingAccount">The impersonation account identifier after authorization has been applied.</param>
    /// <param name="clientAuthorizationHeader">The parsed inbound authorization header.</param>
    /// <param name="canImpersonate">Whether the current request is allowed to perform impersonation.</param>
    /// <param name="canAdmin">Whether the current request has admin role access.</param>
    public ApiRequestContext(
        string? accountId,
        string? authorizedImpersonatingAccount,
        AuthenticationHeaderValue? clientAuthorizationHeader,
        bool canImpersonate,
        bool canAdmin)
    {
        AccountId = accountId?.Trim() ?? string.Empty;
        ClientAuthorizationHeader = clientAuthorizationHeader;
        CanImpersonate = canImpersonate;
        CanAdmin = canAdmin;

        ImpersonatingAccount = CanImpersonate
            ? NormalizeImpersonatingAccount(authorizedImpersonatingAccount)
            : string.Empty;
    }

    /// <summary>
    /// Gets the signed-in account identifier.
    /// </summary>
    public string AccountId { get; }

    /// <summary>
    /// Gets the impersonation target when the current user is allowed to impersonate.
    /// </summary>
    public string ImpersonatingAccount { get; }

    /// <summary>
    /// Gets the effective account for downstream service and repository calls.
    /// </summary>
    public string ActiveAccount => string.IsNullOrWhiteSpace(ImpersonatingAccount)
        ? AccountId
        : ImpersonatingAccount;

    /// <summary>
    /// Gets the parsed client authorization header for the current request.
    /// </summary>
    public AuthenticationHeaderValue? ClientAuthorizationHeader { get; }

    /// <summary>
    /// Gets a value indicating whether the signed-in user can impersonate another account.
    /// </summary>
    public bool CanImpersonate { get; }

    /// <summary>
    /// Gets a value indicating whether the active account can administer the application.
    /// </summary>
    public bool CanAdmin { get; }

    private static string NormalizeImpersonatingAccount(string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return string.Empty;
        }

        var trimmedAccountId = accountId.Trim();
        return long.TryParse(trimmedAccountId, out _)
            ? trimmedAccountId
            : string.Empty;
    }
}
