using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.Mvc;
using FeatureFlags = SystemUptimeTracker.Common.Constants.FeatureFlags;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Api.Helpers.Attributes;
using SystemUptimeTracker.Api.Helpers.Extensions;
using SystemUptimeTracker.Api.Helpers.Interfaces;
using SystemUptimeTracker.Api.Models.ApplicationSettings;
using SystemUptimeTracker.Api.Services.Info.Interface;
using System.Collections;
using System.Net;
using System.Text;

namespace SystemUptimeTracker.Api.Controllers;

/// <summary>
/// Exposes informational endpoints for the running service such as status and configuration details.
/// </summary>
/// <remarks>
/// The endpoints in this controller are feature-flagged and may be disabled in production.
/// Use these endpoints for health checks, diagnostics and debugging purposes only.
/// </remarks>
[DebugLevelLogger<InfoController>]
[ApiController]
[FeatureGate(FeatureFlags.INFO_ENDPOINT_ENABLED)]
[Route("api/[controller]")]
public class InfoController : BaseApiController<InfoController>
{
    private readonly IInfoService _infoService;
    private readonly IFeatureManager _featureManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="InfoController"/> class.
    /// </summary>
    /// <param name="commonDependencies">Common controller dependencies provided by DI.</param>
    /// <param name="logger">Logger instance for the controller.</param>
    /// <param name="featureManager">Feature manager used to gate sensitive endpoints.</param>
    /// <param name="infoService">Service that produces status and diagnostic payloads.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="featureManager"/> or <paramref name="infoService"/> is null.</exception>
    public InfoController(
        IControllerDependencyBundle commonDependencies,
        ILogger<InfoController> logger,
        IFeatureManager featureManager,
        IInfoService infoService) : base(commonDependencies, logger)
    {
        _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
        _infoService = infoService ?? throw new ArgumentNullException(nameof(infoService));
    }

    /// <summary>
    /// Gets service status information serialized as XML.
    /// </summary>
    /// <remarks>
    /// This endpoint returns the status of the service, including health check results and other diagnostics,
    /// serialized in XML format.
    /// </remarks>
    /// <returns>
    /// A <see cref="ContentResult"/> containing the status payload serialized to XML with content type "application/xml" and UTF-8 encoding.
    /// </returns>
    [AllowAnonymous]
    [HttpGet("StatusXml")]
    [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any)]
    [Produces("application/xml")]
    [ProducesResponseType(typeof(ContentResult), StatusCodes.Status200OK)]
    public async Task<ContentResult> GetStatusInformationXml()
    {
        await Task.Yield();
        using IDisposable? scope = BeginOperationScope(nameof(GetStatusInformationXml));

        Logger.LogInformation("Serving status XML payload.");
        return Content(_infoService.SerializeToResponseXml(), "application/xml", Encoding.UTF8);
    }

    /// <summary>
    /// Gets service status information serialized as JSON.
    /// </summary>
    /// <remarks>
    /// This endpoint returns the status of the service, including health check results and other diagnostics,
    /// serialized in JSON format.
    /// </remarks>
    /// <returns>
    /// A <see cref="ContentResult"/> containing the status payload serialized to JSON with content type "application/json" and UTF-8 encoding.
    /// </returns>
    [AllowAnonymous]
    [HttpGet("StatusJson")]
    [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ContentResult), StatusCodes.Status200OK)]
    public async Task<ContentResult> GetStatusInformationJson()
    {
        await Task.Yield();
        using IDisposable? scope = BeginOperationScope(nameof(GetStatusInformationJson));

        Logger.LogInformation("Serving status JSON payload.");
        return Content(_infoService.SerializeToResponseJson()!, "application/json", Encoding.UTF8);
    }

    /// <summary>
    /// Returns application configuration settings (from appsettings) when configuration info feature is enabled.
    /// </summary>
    /// <remarks>
    /// This endpoint is intended for diagnostic use. Access is gated by the <c>CONFIGURATION_INFO_ENABLED</c> feature flag.
    /// </remarks>
    /// <returns>
    /// An <see cref="ActionResult{T}"/> containing <see cref="AppSettings"/> with an HTTP 200 when the feature is enabled, or HTTP 403 when it is disabled.
    /// </returns>
    [Authorize(Policy = AuthorizationPolicyNames.CAN_MANAGE_USERS)]
    [HttpGet("Settings")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AppSettings), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AppSettings>> GetAppSettings()
    {
        await Task.Yield();
        using IDisposable? scope = BeginOperationScope(nameof(GetAppSettings));

        Logger.LogAppSettings(AppSettings);
        if (await _featureManager.IsEnabledAsync(FeatureFlags.CONFIGURATION_INFO_ENABLED))
        {
            Logger.LogInformation("Serving application settings payload.");
            return Ok(AppSettings);
        }
        Logger.LogWarning("Blocked application settings request because the configuration info feature flag is disabled.");
        return Forbid();
    }

    /// <summary>
    /// Returns the current process environment variables when configuration info feature is enabled.
    /// </summary>
    /// <remarks>
    /// This endpoint provides access to the environment variables of the process running the application,
    /// which can be useful for debugging and diagnostic purposes.
    /// </remarks>
    /// <returns>
    /// An <see cref="ActionResult{T}"/> containing an <see cref="IDictionary"/> of environment variables (HTTP 200) or HTTP 403 when the feature is disabled.
    /// </returns>
    [Authorize(Policy = AuthorizationPolicyNames.CAN_MANAGE_USERS)]
    [HttpGet("Environment")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IDictionary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IDictionary>> GetEnvironment()
    {
        await Task.Yield();
        using IDisposable? scope = BeginOperationScope(nameof(GetEnvironment));

        if (await _featureManager.IsEnabledAsync(FeatureFlags.CONFIGURATION_INFO_ENABLED))
        {
            Logger.LogInformation("Serving environment variables payload.");
            return Ok(Environment.GetEnvironmentVariables());
        }
        Logger.LogWarning("Blocked environment request because the configuration info feature flag is disabled.");
        return Forbid();
    }
}
