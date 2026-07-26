using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SystemUptimeTracker.Api.Controllers;
using SystemUptimeTracker.Api.Constants.Enums;
using SystemUptimeTracker.Api.Helpers.Attributes;
using SystemUptimeTracker.Api.Helpers.Interfaces;
using SystemUptimeTracker.Api.Models.ApplicationSettings;
using System.Security.Claims;

namespace SystemUptimeTracker.Tests.Helpers.Attributes;

[TestFixture(Category = "Unit")]
public class ValidateUserAsAdminAttributeTests
{
    [Test]
    public void OnActionExecuting_WhenUserIsNotAdmin_SetsUnauthorizedResultBeforeActionRuns()
    {
        var filter = new ValidateUserAsAdminAttribute<TestController>(allow: false);
        var controller = CreateController(canAdmin: false);
        var context = CreateActionExecutingContext(controller);

        filter.OnActionExecuting(context);

        Assert.That(context.Result, Is.TypeOf<UnauthorizedObjectResult>());
    }

    [Test]
    public void OnActionExecuting_WhenUserIsAdmin_AllowsActionToProceed()
    {
        var filter = new ValidateUserAsAdminAttribute<TestController>(allow: false);
        var controller = CreateController(canAdmin: true);
        var context = CreateActionExecutingContext(controller);

        filter.OnActionExecuting(context);

        Assert.That(context.Result, Is.Null);
    }

    private static ActionExecutingContext CreateActionExecutingContext(TestController controller)
    {
        return new ActionExecutingContext(
            new ActionContext(
                controller.HttpContext,
                new RouteData(),
                new ControllerActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller);
    }

    private static TestController CreateController(bool canAdmin)
    {
        var dependencies = Substitute.For<IControllerDependencyBundle>();
        dependencies.AppSettings.Returns(new AppSettings
        {
            ImpersonatingCookie = "acting-as"
        });

        var controller = new TestController(dependencies);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(canAdmin)
        };

        return controller;
    }

    private static HttpContext CreateHttpContext(bool canAdmin)
    {
        var context = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "123")
        };

        if (canAdmin)
        {
            claims.Add(new Claim("roles", AccessControlPermission.CAN_ADMIN_APPLICATION.ToString()));
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        context.RequestServices = CreateRequestServices();
        return context;
    }

    private static IServiceProvider CreateRequestServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns(Environments.Development);
        services.AddSingleton(hostEnvironment);
        return services.BuildServiceProvider();
    }

    private sealed class TestController : BaseApiController<TestController>
    {
        public TestController(IControllerDependencyBundle commonDependencies)
            : base(commonDependencies, NullLogger<TestController>.Instance)
        {
        }
    }
}
