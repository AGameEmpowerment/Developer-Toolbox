using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Api.Helpers.Tracing;
using SystemUptimeTracker.Api.Models.Auth;
using System.Globalization;

namespace SystemUptimeTracker.Api.Extensions;

public static class AntiforgeryEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the same-origin browser endpoint that issues request tokens for cookie-authenticated writes.
    /// </summary>
    public static IEndpointRouteBuilder MapSystemUptimeTrackerAntiforgeryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/antiforgery-token", (HttpContext httpContext, IAntiforgery antiforgery) =>
            {
                DisableTokenResponseCaching(httpContext);

                AntiforgeryTokenSet tokenSet = antiforgery.GetAndStoreTokens(httpContext);

                if (string.IsNullOrWhiteSpace(tokenSet.RequestToken))
                {
                    ProblemDetails problemDetails = new()
                    {
                        Status = StatusCodes.Status500InternalServerError,
                        Title = "Antiforgery token generation failed.",
                        Detail = RequestTraceContext.BuildUserMessage(
                            httpContext,
                            "The server could not generate an antiforgery token."),
                        Type = "Internal Server Error"
                    };

                    RequestTraceContext.EnrichProblemDetails(httpContext, problemDetails);
                    httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

                    return Results.Problem(problemDetails);
                }

                string headerName = tokenSet.HeaderName ?? SystemUptimeTrackerAuthenticationSchemes.ANTIFORGERY_HEADER_NAME;

                return Results.Ok(new AntiforgeryTokenResponse(
                    tokenSet.RequestToken,
                    headerName));
            })
            .RequireAuthorization(AuthorizationPolicyNames.AUTHENTICATED_USER);

        endpoints.MapGet(
                "/api/auth/permissions",
                async (HttpContext httpContext, IAuthorizationService authorizationService) =>
                {
                    DisableTokenResponseCaching(httpContext);

                    return Results.Ok(new AuthorizationPoliciesResponse(
                        await IsAuthorizedAsync(httpContext, authorizationService, AuthorizationPolicyNames.CAN_MANAGE_USERS)));
                })
            .RequireAuthorization(AuthorizationPolicyNames.AUTHENTICATED_USER);

        return endpoints;
    }

    private static async Task<bool> IsAuthorizedAsync(
        HttpContext httpContext,
        IAuthorizationService authorizationService,
        string policyName)
    {
        return (await authorizationService.AuthorizeAsync(httpContext.User, policyName)).Succeeded;
    }

    private static void DisableTokenResponseCaching(HttpContext httpContext)
    {
        httpContext.Response.Headers[HeaderNames.CacheControl] = "no-store, no-cache";
        httpContext.Response.Headers[HeaderNames.Pragma] = "no-cache";
        httpContext.Response.Headers[HeaderNames.Expires] = DateTimeOffset.UnixEpoch.ToString("R", CultureInfo.InvariantCulture);
    }
}
