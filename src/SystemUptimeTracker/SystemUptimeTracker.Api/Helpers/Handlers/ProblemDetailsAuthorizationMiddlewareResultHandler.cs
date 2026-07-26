using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using SystemUptimeTracker.Api.Helpers.Tracing;

namespace SystemUptimeTracker.Api.Helpers.Handlers;

public sealed class ProblemDetailsAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly IProblemDetailsService _problemDetailsService;

    public ProblemDetailsAuthorizationMiddlewareResultHandler(IProblemDetailsService problemDetailsService)
    {
        _problemDetailsService = problemDetailsService;
    }

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        if (context.Response.HasStarted)
        {
            return;
        }

        bool isChallenge = authorizeResult.Challenged;
        int statusCode = isChallenge ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;
        ProblemDetails problemDetails = new()
        {
            Status = statusCode,
            Title = isChallenge ? "Authentication is required." : "You are not authorized to perform this action.",
            Detail = RequestTraceContext.BuildUserMessage(
                context,
                isChallenge
                    ? "The request requires an authenticated user."
                    : "The current user is not allowed to perform this action."),
            Type = isChallenge ? "Unauthorized" : "Forbidden"
        };

        RequestTraceContext.EnrichProblemDetails(context, problemDetails);
        context.Response.StatusCode = statusCode;

        await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problemDetails
        });
    }
}