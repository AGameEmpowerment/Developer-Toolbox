using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi;
using System.Net;

namespace SystemUptimeTracker.Api.Helpers.OpenApi;

/// <summary>
/// Adds default error responses (400, 401, 404, 500) to controller operations.
/// </summary>
public static class DefaultResponsesOpenApiCustomizer
{
    /// <summary>
    /// Adds default error responses for controller endpoints.
    /// </summary>
    public static void Apply(OpenApiOperation operation, ApiDescription description)
    {
        // Only apply to controller-based endpoints
        if (description.ActionDescriptor is not Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor controllerDescriptor)
        {
            return;
        }

        var declaringType = controllerDescriptor.ControllerTypeInfo;

        // Check if it's a controller-based action
        var isController = typeof(ControllerBase).IsAssignableFrom(declaringType);
        if (!isController)
        {
            return;
        }

        // Add default error responses
        AddResponseIfNotExists(operation, HttpStatusCode.InternalServerError, "500 - See Error Results for Details");
        AddResponseIfNotExists(operation, HttpStatusCode.BadRequest, "400 - See Error Results for Details");
        AddResponseIfNotExists(operation, HttpStatusCode.NotFound, "404 - See Error Results for Details");
        AddResponseIfNotExists(operation, HttpStatusCode.Unauthorized, "401");

    }

    private static void AddResponseIfNotExists(
        OpenApiOperation operation,
        HttpStatusCode statusCode,
        string description)
    {
        var statusCodeString = ((int)statusCode).ToString();

        if (operation.Responses?.ContainsKey(statusCodeString) == true)
        {
            return;
        }

        operation.Responses ??= new OpenApiResponses();

        var response = new OpenApiResponse
        {
            Description = description
        };

        operation.Responses.Add(statusCodeString, response);
    }
}
