using SystemUptimeTracker.Api.Constants;

namespace SystemUptimeTracker.Api.Extensions;

public static class ProtectedApiEndpointConventionBuilderExtensions
{
    public static TBuilder RequireUserManagementPolicy<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization(AuthorizationPolicyNames.CAN_MANAGE_USERS);
        return builder;
    }
}
