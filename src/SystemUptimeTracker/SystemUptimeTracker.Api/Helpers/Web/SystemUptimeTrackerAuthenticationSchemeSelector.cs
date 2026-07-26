using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;

namespace SystemUptimeTracker.Api.Helpers.Web;

public static class SystemUptimeTrackerAuthenticationSchemeSelector
{
    /// <summary>
    /// Routes requests to the concrete authentication handler required by the supported client credential shape.
    /// </summary>
    public static string Resolve(HttpContext context, bool jwtEnabled = false)
    {
        string authorizationHeader = context.Request.Headers.Authorization.ToString();
        if (authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            string token = authorizationHeader["Bearer ".Length..].Trim();
            return jwtEnabled && IsJsonWebToken(token)
                ? JwtBearerDefaults.AuthenticationScheme
                : IdentityConstants.BearerScheme;
        }

        return IdentityConstants.ApplicationScheme;
    }

    private static bool IsJsonWebToken(string token)
    {
        return token.Count(character => character == '.') == 2;
    }
}
