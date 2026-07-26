using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SystemUptimeTracker.Api.Helpers.Attributes;

public class ValidateUserAsAdminAttribute<T> : ActionFilterAttribute
{
    private bool _allow;

    /// <summary>
    ///
    /// </summary>
    public ValidateUserAsAdminAttribute(bool allow)
    {
        _allow = allow;
    }

    /// <summary>
    /// Validates that the current user has admin access based on role-derived request context.
    /// </summary>
    /// <param name="context"></param>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var baseApiController = context.Controller as Controllers.BaseApiController<T>;
        bool canAdmin = _allow || baseApiController?.CanAdmin == true;

        if (!canAdmin)
        {
            context.Result = new UnauthorizedObjectResult("Insufficient privileges");
            return;
        }

        base.OnActionExecuting(context);
    }
}
