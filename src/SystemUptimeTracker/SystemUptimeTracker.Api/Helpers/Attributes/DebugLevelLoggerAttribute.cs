using Microsoft.AspNetCore.Mvc.Filters;
using SystemUptimeTracker.Api.Controllers;

namespace SystemUptimeTracker.Api.Helpers.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
public class DebugLevelLoggerAttribute<T> : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.Controller is BaseApiController<T> baseController)
        {
            ILogger<T> logger = baseController.Logger;
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(Common.Constants.LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, typeof(T).Name, context.ActionDescriptor.DisplayName);
            }
        }

        base.OnActionExecuting(context);
    }
}