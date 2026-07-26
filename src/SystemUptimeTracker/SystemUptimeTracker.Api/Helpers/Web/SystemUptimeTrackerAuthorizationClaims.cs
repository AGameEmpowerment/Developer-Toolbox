using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Data.Identity;
using System.Security.Claims;

namespace SystemUptimeTracker.Api.Helpers.Web;

public static class SystemUptimeTrackerAuthorizationClaims
{
    /// <summary>
    /// Determines whether the authenticated principal came from either supported local Identity scheme.
    /// </summary>
    public static bool IsLocalIdentityPrincipal(ClaimsPrincipal principal)
    {
        return IsCookieIdentityPrincipal(principal) ||
               principal.Identities.Any(identity =>
                   string.Equals(identity.AuthenticationType, IdentityConstants.BearerScheme, StringComparison.Ordinal) ||
                   string.Equals(identity.AuthenticationType, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal));
    }

    /// <summary>
    /// Determines whether the authenticated principal came from the local Identity application cookie.
    /// </summary>
    public static bool IsCookieIdentityPrincipal(ClaimsPrincipal principal)
    {
        return principal.Identities.Any(identity =>
            string.Equals(identity.AuthenticationType, IdentityConstants.ApplicationScheme, StringComparison.Ordinal));
    }

    public static bool IsActiveLocalIdentityPrincipal(ClaimsPrincipal principal)
    {
        if (!IsLocalIdentityPrincipal(principal))
        {
            return false;
        }

        string? isActive = principal.FindFirstValue(SystemUptimeTrackerClaimTypes.IS_ACTIVE);
        return string.IsNullOrWhiteSpace(isActive)
               || string.Equals(isActive, bool.TrueString, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the authenticated local Identity account identifier.
    /// </summary>
    public static string? ResolveSignedInAccountId(ClaimsPrincipal? principal)
    {
        return principal is not null && IsLocalIdentityPrincipal(principal)
            ? principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub")
            : null;
    }

    /// <summary>
    /// Resolves the local Identity user represented by the authenticated principal.
    /// </summary>
    public static async Task<ApplicationUser?> ResolveLinkedUserAsync(UserManager<ApplicationUser> userManager, ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true || !IsLocalIdentityPrincipal(principal))
        {
            return null;
        }

        string? accountId = ResolveSignedInAccountId(principal);
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            ApplicationUser? byId = await userManager.FindByIdAsync(accountId);
            if (byId is not null)
            {
                return byId;
            }
        }

        return null;
    }
}
