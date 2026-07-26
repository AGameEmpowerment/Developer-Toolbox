using SystemUptimeTracker.Api.Helpers.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace SystemUptimeTracker.Tests.Helpers.Web;

[TestFixture(Category = "Unit")]
public class SystemUptimeTrackerAuthorizationClaimsTests
{
    [Test]
    public void ResolveSignedInAccountId_WhenNameIdentifierExists_ReturnsNameIdentifier()
    {
        var principal = CreatePrincipal(
            IdentityConstants.ApplicationScheme,
            new Claim(ClaimTypes.NameIdentifier, "local-user"),
            new Claim("sub", "jwt-user"));

        string? accountId = SystemUptimeTrackerAuthorizationClaims.ResolveSignedInAccountId(principal);

        Assert.That(accountId, Is.EqualTo("local-user"));
    }

    [Test]
    public void ResolveSignedInAccountId_WhenJwtSubjectExists_ReturnsSubject()
    {
        var principal = CreatePrincipal(
            JwtBearerDefaults.AuthenticationScheme,
            new Claim("sub", "subject-user"));

        string? accountId = SystemUptimeTrackerAuthorizationClaims.ResolveSignedInAccountId(principal);

        Assert.That(accountId, Is.EqualTo("subject-user"));
    }

    [Test]
    public void ResolveSignedInAccountId_WhenPrincipalUsesUnsupportedScheme_ReturnsNull()
    {
        var principal = CreatePrincipal("External", new Claim(ClaimTypes.NameIdentifier, "external-user"));

        string? accountId = SystemUptimeTrackerAuthorizationClaims.ResolveSignedInAccountId(principal);

        Assert.That(accountId, Is.Null);
    }

    private static ClaimsPrincipal CreatePrincipal(string authenticationType, params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }
}
