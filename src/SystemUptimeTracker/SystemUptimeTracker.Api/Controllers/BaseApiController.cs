using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Api.Helpers.Interfaces;
using SystemUptimeTracker.Api.Helpers.Tracing;
using SystemUptimeTracker.Api.Helpers.Web;
using SystemUptimeTracker.Api.Models.ApplicationSettings;
using SystemUptimeTracker.Api.Models.RequestContext;
using SystemUptimeTracker.Api.Constants.Enums;
using System.Net.Http.Headers;
using System.Security.Claims;

namespace SystemUptimeTracker.Api.Controllers;

public class BaseApiController<T> : ControllerBase
{
    private ApiRequestContext? _requestContext;

    public ILogger<T> Logger { get; set; }

    // ReSharper disable once ConvertToPrimaryConstructor
    public BaseApiController(IControllerDependencyBundle commonDependencies, ILogger<T> logger)
    {
        AppSettings = commonDependencies.AppSettings;
        Logger = logger;
    }

    public AppSettings AppSettings { get; set; }

    // Materialize the request contract once so downstream controller logic reads stable request-scoped values.
    private ApiRequestContext RequestContext => _requestContext ??= BuildRequestContext();

    public string SignedInWithAccountId => AccountId;

    public string AccountId => RequestContext.AccountId;

    public string ImpersonatingAccount => RequestContext.ImpersonatingAccount;

    public string ImpersonatingAccountId => ImpersonatingAccount;

    public string ActiveAccount => RequestContext.ActiveAccount;

    public bool CanAdmin => RequestContext.CanAdmin;

    public bool CanImpersonate => RequestContext.CanImpersonate;

    public bool IsImpersonating => !string.IsNullOrWhiteSpace(ImpersonatingAccount);

    public AuthenticationHeaderValue? ClientAuthorizationHeader => RequestContext.ClientAuthorizationHeader;

    public long AccountIdLong => long.TryParse(AccountId, out long value) ? value : 0;

    public long ActiveAccountLong => long.TryParse(ActiveAccount, out long value) ? value : 0;

    private ApiRequestContext BuildRequestContext()
    {
        var signedInAccountId = ResolveSignedInAccountId();
        var canAdmin = HasRole(AccessControlPermission.CAN_ADMIN_APPLICATION);
        var canImpersonate = IsImpersonationEnabled() &&
                             (canAdmin || HasRole(AccessControlPermission.CAN_IMPERSONATE));
        var authorizedImpersonatingAccount = ResolveAuthorizedImpersonationTarget(
            signedInAccountId,
            ResolveRequestedImpersonatingAccount(),
            canImpersonate);

        Logger.LogDebug(
            "Request context created for {ControllerName}. Path: {RequestPath}; SignedIn: {SignedIn}; CanImpersonate: {CanImpersonate}; IsImpersonating: {IsImpersonating}",
            typeof(T).Name,
            HttpContext?.Request?.Path.Value ?? string.Empty,
                !string.IsNullOrWhiteSpace(signedInAccountId),
            canImpersonate,
                !string.IsNullOrWhiteSpace(authorizedImpersonatingAccount));

        return new ApiRequestContext(
                signedInAccountId,
                authorizedImpersonatingAccount,
            HttpContext.GetClientAuthorizationHeader(),
            canImpersonate,
            canAdmin);
    }

            private string ResolveSignedInAccountId()
    {
        try
        {
            string? account = HttpContext?.User?.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(account))
            {
                Logger.LogDebug(
                    "No signed-in account id was found for {ControllerName}. Path: {RequestPath}",
                    typeof(T).Name,
                    HttpContext?.Request?.Path.Value ?? string.Empty);
            }

            return string.IsNullOrWhiteSpace(account) ? string.Empty : account.Trim();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Failed to resolve signed-in account id for {ControllerName}.",
                typeof(T).Name);
            return string.Empty;
        }
    }

    private string ResolveRequestedImpersonatingAccount()
    {
        try
        {
            if (HttpContext?.Request?.Headers is null)
            {
                return string.Empty;
            }

            var impersonationHeader = HttpContext.Request.Headers
                .FirstOrDefault(c => c.Key == AppSettings.ImpersonatingCookie)
                .Value
                .ToString();

            if (string.IsNullOrWhiteSpace(impersonationHeader))
            {
                return string.Empty;
            }

            var trimmedAccountId = impersonationHeader.Trim();
            if (!long.TryParse(trimmedAccountId, out _))
            {
                Logger.LogWarning(
                    "Ignoring invalid impersonation header for {ControllerName}. Path: {RequestPath}",
                    typeof(T).Name,
                    HttpContext?.Request?.Path.Value ?? string.Empty);
                return string.Empty;
            }

            return trimmedAccountId;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Failed to resolve impersonation target for {ControllerName}.",
                typeof(T).Name);
            return string.Empty;
        }
    }

    private bool IsImpersonationEnabled()
    {
        return HttpContext?.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true;
    }

    private bool HasRole(AccessControlPermission permission)
    {
        return HttpContext?.User?.Claims.Any(claim =>
            (claim.Type == ClaimTypes.Role || claim.Type == "roles") &&
            string.Equals(claim.Value, permission.ToString(), StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string ResolveAuthorizedImpersonationTarget(
        string signedInAccountId,
        string requestedImpersonationAccountId,
        bool canImpersonate)
    {
        if (!canImpersonate ||
            string.IsNullOrWhiteSpace(requestedImpersonationAccountId) ||
            string.Equals(signedInAccountId, requestedImpersonationAccountId, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return requestedImpersonationAccountId;
    }

    protected IDisposable? BeginOperationScope(string operationName)
    {
        return HttpContext is null
            ? null
            : Logger.BeginScope(RequestTraceContext.CreateLogScope(HttpContext, typeof(T).Name, operationName));
    }

}
