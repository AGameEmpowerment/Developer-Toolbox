using System.Diagnostics.CodeAnalysis;

namespace SystemUptimeTracker.Api.Constants;

[ExcludeFromCodeCoverage]
public static class AuthorizationPolicyNames
{
    public const string AUTHENTICATED_USER = nameof(AUTHENTICATED_USER);
    public const string CAN_MANAGE_USERS = nameof(CAN_MANAGE_USERS);
}
