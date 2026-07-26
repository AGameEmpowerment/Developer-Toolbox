using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SystemUptimeTracker.Api.Controllers;
using SystemUptimeTracker.Api.Helpers.Interfaces;
using SystemUptimeTracker.Api.Helpers.Tracing;
using SystemUptimeTracker.Api.Models.ApplicationSettings;
using SystemUptimeTracker.Api.Models.Operations;
using SystemUptimeTracker.Api.Services.Operations.Interface;

namespace SystemUptimeTracker.Tests.Controllers;

[TestFixture(Category = "Unit")]
public class OperationsControllerTests
{
    private const string TESTING_ENVIRONMENT = "Testing";

    private IControllerDependencyBundle _dependencies = null!;
    private IHostEnvironment _hostEnvironment = null!;
    private IOperationsMetadataService _operationsMetadataService = null!;
    private OperationsController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _dependencies = Substitute.For<IControllerDependencyBundle>();
        _dependencies.AppSettings.Returns(new AppSettings());

        _hostEnvironment = Substitute.For<IHostEnvironment>();
        _hostEnvironment.ApplicationName.Returns("SystemUptimeTracker.Api");
        _hostEnvironment.EnvironmentName.Returns(TESTING_ENVIRONMENT);

        _operationsMetadataService = Substitute.For<IOperationsMetadataService>();
        _operationsMetadataService.GetMetadata().Returns(new OperationsMetadataResponse
        {
            ApplicationName = "SystemUptimeTracker.Api",
            ApplicationVersion = "1.2.3",
            BuildVersion = "1.2.3.4",
            Environment = TESTING_ENVIRONMENT,
            StartedAtUtc = new DateTimeOffset(2026, 4, 17, 12, 0, 0, TimeSpan.Zero)
        });

        _controller = new OperationsController(
            _dependencies,
            NullLogger<OperationsController>.Instance,
            _hostEnvironment,
            _operationsMetadataService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-request-001",
                    RequestServices = new ServiceCollection()
                        .AddSingleton(_hostEnvironment)
                        .BuildServiceProvider()
                }
            }
        };

        _controller.HttpContext.Request.Method = HttpMethods.Get;
        _controller.HttpContext.Request.Path = "/api/operations/validate-error-shape";
    }

    [Test]
    public void GetMetadata_ReturnsOkWithOperationsMetadata()
    {
        var result = _controller.GetMetadata().Result as OkObjectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value, Is.TypeOf<OperationsMetadataResponse>());

        var payload = (OperationsMetadataResponse)result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(payload.ApplicationName, Is.EqualTo("SystemUptimeTracker.Api"));
            Assert.That(payload.ApplicationVersion, Is.EqualTo("1.2.3"));
            Assert.That(payload.Environment, Is.EqualTo(TESTING_ENVIRONMENT));
        });
    }

    [Test]
    public void ValidateErrorShape_InAllowedEnvironment_ReturnsScrubbedProblemDetails()
    {
        _hostEnvironment.EnvironmentName.Returns(TESTING_ENVIRONMENT);

        var result = _controller.ValidateErrorShape() as ObjectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        Assert.That(result.ContentTypes, Does.Contain("application/problem+json"));
        Assert.That(result.Value, Is.TypeOf<ProblemDetails>());

        var problemDetails = (ProblemDetails)result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(problemDetails.Title, Is.EqualTo("An unexpected error occurred."));
            Assert.That(problemDetails.Detail, Does.Contain("trace-request-001"));
            Assert.That(problemDetails.Detail, Does.Not.Contain("SELECT * FROM dbo.Users"));
            Assert.That(problemDetails.Detail, Does.Not.Contain("C:\\systemuptimetracker\\assets\\secret"));
            Assert.That(problemDetails.Extensions[RequestTraceContext.TRACE_ID_KEY], Is.EqualTo("trace-request-001"));
            Assert.That(problemDetails.Extensions[RequestTraceContext.REQUEST_ID_KEY], Is.EqualTo("trace-request-001"));
            Assert.That(_controller.HttpContext.Response.Headers[RequestTraceContext.TRACE_ID_HEADER_NAME].ToString(), Is.EqualTo("trace-request-001"));
        });
    }

    [Test]
    public void ValidateErrorShape_InDisallowedEnvironment_ReturnsNotFound()
    {
        _hostEnvironment.EnvironmentName.Returns("Production");

        var result = _controller.ValidateErrorShape();

        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
}
