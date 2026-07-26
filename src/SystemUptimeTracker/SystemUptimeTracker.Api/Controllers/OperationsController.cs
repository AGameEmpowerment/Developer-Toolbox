using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SystemUptimeTracker.Api.Helpers.Attributes;
using SystemUptimeTracker.Api.Helpers.Interfaces;
using SystemUptimeTracker.Api.Helpers.Tracing;
using SystemUptimeTracker.Api.Models.Operations;
using SystemUptimeTracker.Api.Services.Operations.Interface;

namespace SystemUptimeTracker.Api.Controllers;

[DebugLevelLogger<OperationsController>]
[ApiController]
[Route("api/[controller]")]
public class OperationsController : BaseApiController<OperationsController>
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOperationsMetadataService _operationsMetadataService;

    public OperationsController(
        IControllerDependencyBundle commonDependencies,
        ILogger<OperationsController> logger,
        IHostEnvironment hostEnvironment,
        IOperationsMetadataService operationsMetadataService) : base(commonDependencies, logger)
    {
        _hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
        _operationsMetadataService = operationsMetadataService ?? throw new ArgumentNullException(nameof(operationsMetadataService));
    }

    [AllowAnonymous]
    [HttpGet("metadata")]
    [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(OperationsMetadataResponse), StatusCodes.Status200OK)]
    public ActionResult<OperationsMetadataResponse> GetMetadata()
    {
        using IDisposable? scope = BeginOperationScope(nameof(GetMetadata));

        Logger.LogInformation("Serving operations metadata payload.");
        return Ok(_operationsMetadataService.GetMetadata());
    }

    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    [HttpGet("validate-error-shape")]
    [Produces("application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult ValidateErrorShape()
    {
        using IDisposable? scope = BeginOperationScope(nameof(ValidateErrorShape));

        if (!_hostEnvironment.IsDevelopment() &&
            !_hostEnvironment.IsEnvironment("Automation") &&
            !_hostEnvironment.IsEnvironment("Testing"))
        {
            Logger.LogWarning(
                "Blocked validate-error-shape request outside allowed environments. Environment: {EnvironmentName}",
                _hostEnvironment.EnvironmentName);

            return NotFound();
        }

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = RequestTraceContext.BuildUserMessage(HttpContext, "The request could not be completed.")
        };

        RequestTraceContext.EnrichProblemDetails(HttpContext, problemDetails);

        Logger.LogInformation(
            "Returning controlled validate-error-shape problem details payload in {EnvironmentName}.",
            _hostEnvironment.EnvironmentName);

        return new ObjectResult(problemDetails)
        {
            StatusCode = StatusCodes.Status500InternalServerError,
            ContentTypes = { "application/problem+json" }
        };
    }
}
