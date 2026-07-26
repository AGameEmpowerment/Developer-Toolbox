using Microsoft.AspNetCore.Authorization;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Api.Helpers.Web;
using System.Security.Claims;

namespace SystemUptimeTracker.Api.Authorization;

public static class SystemUptimeTrackerAuthorizationPolicyCatalog
{
    private static readonly IReadOnlyDictionary<string, string[]> _roleBackedPolicies = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [AuthorizationPolicyNames.CAN_MANAGE_USERS] = [ApplicationRoleNames.ADMIN]
    };

    public static IEnumerable<string> RoleBackedPolicyNames => _roleBackedPolicies.Keys;

    public static void Configure(AuthorizationOptions options)
    {
        options.DefaultPolicy = new AuthorizationPolicyBuilder(SystemUptimeTrackerAuthenticationSchemes.APPLICATION)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => SystemUptimeTrackerAuthorizationClaims.IsActiveLocalIdentityPrincipal(context.User))
            .Build();

        options.AddPolicy(AuthorizationPolicyNames.AUTHENTICATED_USER, policy =>
        {
            policy.AddAuthenticationSchemes(SystemUptimeTrackerAuthenticationSchemes.APPLICATION);
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context => SystemUptimeTrackerAuthorizationClaims.IsActiveLocalIdentityPrincipal(context.User));
        });

        foreach ((string policyName, string[] allowedRoles) in _roleBackedPolicies)
        {
            options.AddPolicy(policyName, policy =>
            {
                policy.AddAuthenticationSchemes(SystemUptimeTrackerAuthenticationSchemes.APPLICATION);
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasRoleBackedAccess(context.User, allowedRoles));
            });
        }
    }

    public static bool HasRoleBackedAccess(ClaimsPrincipal user, IReadOnlyCollection<string> allowedRoles)
    {
        if (!HasAnyRole(user, allowedRoles))
        {
            return false;
        }

        return SystemUptimeTrackerAuthorizationClaims.IsActiveLocalIdentityPrincipal(user);
    }

    private static bool HasAnyRole(ClaimsPrincipal user, IReadOnlyCollection<string> allowedRoles)
    {
        return user.Claims.Any(claim =>
            (claim.Type == ClaimTypes.Role || claim.Type == "roles") &&
            allowedRoles.Contains(claim.Value, StringComparer.OrdinalIgnoreCase));
    }
}
