using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using SystemUptimeTracker.Api.Helpers.Tracing;
using SystemUptimeTracker.Api.Helpers.Web;

namespace SystemUptimeTracker.Api.Helpers.Middleware;

/// <summary>
/// Validates antiforgery tokens for unsafe requests authenticated by the local Identity cookie.
/// </summary>
public sealed class CookieAuthenticatedAntiforgeryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CookieAuthenticatedAntiforgeryMiddleware> _logger;
    private readonly IProblemDetailsService _problemDetailsService;

    public CookieAuthenticatedAntiforgeryMiddleware(
        RequestDelegate next,
        ILogger<CookieAuthenticatedAntiforgeryMiddleware> logger,
        IProblemDetailsService problemDetailsService)
    {
        _next = next;
        _logger = logger;
        _problemDetailsService = problemDetailsService;
    }

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!RequiresAntiforgeryValidation(context))
        {
            await _next(context);
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            _logger.LogInformation(
                "Rejected unsafe cookie-authenticated request because antiforgery validation failed. Path: {RequestPath}; Method: {RequestMethod}",
                context.Request.Path,
                context.Request.Method);

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Antiforgery validation failed.",
                Detail = RequestTraceContext.BuildUserMessage(
                    context,
                    "Unsafe browser requests authenticated by cookies must include a valid antiforgery token."),
                Type = "Bad Request"
            };

            RequestTraceContext.EnrichProblemDetails(context, problemDetails);
            await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problemDetails
            });
            return;
        }

        await _next(context);
    }

    private static bool RequiresAntiforgeryValidation(HttpContext context)
    {
        return IsUnsafeMethod(context.Request.Method) &&
               SystemUptimeTrackerAuthorizationClaims.IsCookieIdentityPrincipal(context.User);
    }

    private static bool IsUnsafeMethod(string method)
    {
        return HttpMethods.IsPost(method) ||
               HttpMethods.IsPut(method) ||
               HttpMethods.IsPatch(method) ||
               HttpMethods.IsDelete(method);
    }
}
