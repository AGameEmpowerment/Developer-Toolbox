using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SystemUptimeTracker.Api.Controllers;
using SystemUptimeTracker.Api.Constants.Enums;
using SystemUptimeTracker.Api.Helpers.Interfaces;
using SystemUptimeTracker.Api.Models.ApplicationSettings;
using System.Security.Claims;

namespace SystemUptimeTracker.Tests.Controllers;

[TestFixture(Category = "Unit")]
public class BaseApiControllerTests
{
    private IControllerDependencyBundle _dependencies;
    private TestApiController _controller;

    [SetUp]
    public void SetUp()
    {
        _dependencies = Substitute.For<IControllerDependencyBundle>();
        _dependencies.AppSettings.Returns(new AppSettings
        {
            ImpersonatingCookie = "acting-as"
        });

        _controller = new TestApiController(_dependencies, NullLogger<TestApiController>.Instance);
    }

    [Test]
    public void AccountId_WhenNotImpersonating_ReturnsSignedInAccount()
    {
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext("123")
        };

        Assert.That(_controller.AccountId, Is.EqualTo("123"));
        Assert.That(_controller.ActiveAccount, Is.EqualTo("123"));
    }

    [Test]
    public void ClientAuthorizationHeader_WhenAuthorizationHeaderPresent_ReturnsParsedHeader()
    {
        var context = CreateHttpContext("123");
        context.Request.Headers.Authorization = "Bearer token-value";

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = context
        };

        Assert.That(_controller.ClientAuthorizationHeader, Is.Not.Null);
        Assert.That(_controller.ClientAuthorizationHeader?.Scheme, Is.EqualTo("Bearer"));
        Assert.That(_controller.ClientAuthorizationHeader?.Parameter, Is.EqualTo("token-value"));
    }

    [Test]
    public void RequestProperties_WhenHttpContextIsMissing_ReturnSafeDefaults()
    {
        Assert.That(_controller.AccountId, Is.EqualTo(string.Empty));
        Assert.That(_controller.ActiveAccount, Is.EqualTo(string.Empty));
        Assert.That(_controller.ClientAuthorizationHeader, Is.Null);
    }

    [Test]
    public void CanImpersonate_WhenRolePresentOutsideDevelopment_ReturnsFalse()
    {
        var context = CreateHttpContext(
            "123",
            [AccessControlPermission.CAN_IMPERSONATE.ToString()],
            Environments.Production);
        context.Request.Headers["acting-as"] = "456";

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = context
        };

        Assert.That(_controller.CanImpersonate, Is.False);
        Assert.That(_controller.ImpersonatingAccount, Is.EqualTo(string.Empty));
    }

    [Test]
    public void CanImpersonate_WhenRolePresentInDevelopment_ReturnsTrue()
    {
        var context = CreateHttpContext(
            "123",
            [AccessControlPermission.CAN_IMPERSONATE.ToString()],
            Environments.Development);
        context.Request.Headers["acting-as"] = "456";

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = context
        };

        Assert.That(_controller.CanImpersonate, Is.True);
        Assert.That(_controller.ImpersonatingAccount, Is.EqualTo("456"));
        Assert.That(_controller.ActiveAccount, Is.EqualTo("456"));
    }

    private static DefaultHttpContext CreateHttpContext(
        string accountId,
        IEnumerable<string>? roles = null,
        string environmentName = "Production")
    {
        List<Claim> claims =
        [
            new Claim(ClaimTypes.NameIdentifier, accountId)
        ];

        if (roles is not null)
        {
            claims.AddRange(roles.Select(role => new Claim("roles", role)));
        }

        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            "Test"));
        context.RequestServices = CreateRequestServices(environmentName);
        return context;
    }

    private static IServiceProvider CreateRequestServices(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns(environmentName);
        services.AddSingleton(hostEnvironment);
        return services.BuildServiceProvider();
    }

    private sealed class TestApiController : BaseApiController<TestApiController>
    {
        public TestApiController(IControllerDependencyBundle commonDependencies, ILogger<TestApiController> logger)
            : base(commonDependencies, logger)
        {
        }
    }
}
