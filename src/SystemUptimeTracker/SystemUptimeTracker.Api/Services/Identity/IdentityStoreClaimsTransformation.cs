using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Api.Helpers.Web;
using SystemUptimeTracker.Data.Identity;
using System.Security.Claims;

namespace SystemUptimeTracker.Api.Services.Identity;

public sealed class IdentityStoreClaimsTransformation : IClaimsTransformation
{
    private readonly UserManager<ApplicationUser>? _userManager;

    public IdentityStoreClaimsTransformation(UserManager<ApplicationUser>? userManager = null)
    {
        _userManager = userManager;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (_userManager is null
            || principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        ApplicationUser? linkedUser = await SystemUptimeTrackerAuthorizationClaims.ResolveLinkedUserAsync(_userManager, principal);
        if (linkedUser is null)
        {
            return principal;
        }

        IList<string> roles = await _userManager.GetRolesAsync(linkedUser);
        ClaimsPrincipal clonedPrincipal = ClonePrincipal(principal);
        ClaimsIdentity? identity = clonedPrincipal.Identities.FirstOrDefault(identity => identity.IsAuthenticated);
        if (identity is null)
        {
            return principal;
        }

        foreach (ClaimsIdentity clonedIdentity in clonedPrincipal.Identities)
        {
            RemoveManagedClaims(clonedIdentity);
        }

        foreach (string role in roles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
            identity.AddClaim(new Claim("roles", role));
        }

        identity.AddClaim(new Claim(SystemUptimeTrackerClaimTypes.IS_ACTIVE, linkedUser.IsActive.ToString()));
        return clonedPrincipal;
    }

    private static ClaimsPrincipal ClonePrincipal(ClaimsPrincipal principal)
    {
        return new ClaimsPrincipal(principal.Identities.Select(identity => new ClaimsIdentity(identity)));
    }

    private static void RemoveManagedClaims(ClaimsIdentity identity)
    {
        foreach (Claim claim in identity.FindAll(ClaimTypes.Role)
            .Concat(identity.FindAll("roles"))
            .Concat(identity.FindAll(SystemUptimeTrackerClaimTypes.IS_ACTIVE))
            .ToArray())
        {
            identity.RemoveClaim(claim);
        }
    }
}