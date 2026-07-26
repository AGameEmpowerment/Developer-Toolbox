using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Data.Identity;
using System.Security.Claims;

namespace SystemUptimeTracker.Api.Services.Identity;

public sealed class ApplicationUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ClaimsIdentity identity = await base.GenerateClaimsAsync(user);

        Claim? existingClaim = identity.FindFirst(SystemUptimeTrackerClaimTypes.IS_ACTIVE);
        if (existingClaim is not null)
        {
            identity.RemoveClaim(existingClaim);
        }

        identity.AddClaim(new Claim(SystemUptimeTrackerClaimTypes.IS_ACTIVE, user.IsActive.ToString()));
        return identity;
    }
}