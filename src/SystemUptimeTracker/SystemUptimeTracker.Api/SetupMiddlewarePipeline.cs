using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using SystemUptimeTracker.Api.Extensions;
using SystemUptimeTracker.Api.Helpers.Middleware;
using SystemUptimeTracker.Api.Helpers.OpenApi;
using SystemUptimeTracker.Api.Models.ApplicationSettings;
using SystemUptimeTracker.Data.Identity;
using Scalar.AspNetCore;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace SystemUptimeTracker.Api;

public static class SetupMiddlewarePipeline
{
    private static readonly string _swaggerName = "System Uptime Tracker";
    private const string SPA_FALLBACK_FILE = "/index.html";

    // Centralize the OpenAPI route pattern so it can be changed in one place
    private const string OPEN_API_ROUTE_PATTERN = "/openapi/{documentName}.json";
    private static readonly string _openApiV1 = OPEN_API_ROUTE_PATTERN.Replace("{documentName}", "v1");
    private const string SCALAR_V1_ROUTE = "/scalar/v1";
    private const string HEALTH_ROUTE = "/_health";
    public static WebApplication SetupMiddleware(this WebApplication app, AppSettings appSettings)
    {
        string rootRedirectTarget = appSettings.FeatureManagement.OpenApiEnabled ? SCALAR_V1_ROUTE : HEALTH_ROUTE;

        app.UseForwardedHeaders();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseExceptionHandler();

            //app.UseExceptionHandler("/Error");

            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        app.UseCors("FrontendCors");

        app.UseMiddleware<RequestTraceEnrichmentMiddleware>();
        app.UseHttpLogging();

        app.MapGet("/", () => Results.LocalRedirect(rootRedirectTarget)).AllowAnonymous();

        if (appSettings.FeatureManagement.OpenApiEnabled)
        {
            app.MapOpenApiDocument();

            app.UseSwaggerUI(c =>
            {
                c.EnableTryItOutByDefault();
                c.DocExpansion(DocExpansion.None);
                c.EnableFilter();
                c.DisplayRequestDuration();
                c.EnableDeepLinking();
                c.SwaggerEndpoint(_openApiV1, $"{_swaggerName} v1");
                // If CSS breaks on Swagger comment out this line. Updates for Dark mode can be found at: https://github.com/Amoenus/SwaggerDark
                c.InjectStylesheet("/css/SwaggerDark.css");
                c.DocumentTitle = $"{_swaggerName} Swagger UI";
            });

            // Configure Scalar to use the Swagger-generated OpenAPI document
            app.MapScalarApiReference(options =>
            {
                options
                    .WithTitle(_swaggerName)
                    .WithOpenApiRoutePattern(OPEN_API_ROUTE_PATTERN)
                    .AddDocument("v1", $"{_swaggerName} v1");
            });
        }

        app.MapHealthChecks(HEALTH_ROUTE,
            new HealthCheckOptions()
            {
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
                ResultStatusCodes =
                {
                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                    [HealthStatus.Unhealthy] = StatusCodes.Status200OK
                }
            }).AllowAnonymous();

        app.UseMiddleware<CustomExceptionHandlerMiddleware>();

        app.UseAuthentication();
        app.UseMiddleware<CookieAuthenticatedAntiforgeryMiddleware>();
        app.UseAuthorization();
        app.MapSystemUptimeTrackerAntiforgeryEndpoints();
        app.MapSystemUptimeTrackerProtectedApplicationEndpoints();
        RouteGroupBuilder identityGroup = app.MapGroup("/api/identity");
        // Required product behavior: first-run admin creation is available through
        // the controlled bootstrap surface, then later self-created users stay roleless.
        identityGroup.MapSystemUptimeTrackerBootstrapIdentityEndpoints();

        identityGroup.MapIdentityApi<ApplicationUser>();
        identityGroup.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
            {
                await signInManager.SignOutAsync();
                return Results.NoContent();
            })
            .RequireAuthorization();
        app.MapControllers();

        if (CanServeSpaFallback(app))
        {
            app.MapFallbackToFile(SPA_FALLBACK_FILE);
        }

        return app;
    }

    private static bool CanServeSpaFallback(WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Environment.WebRootPath) || !Directory.Exists(app.Environment.WebRootPath))
        {
            return false;
        }

        IFileProvider webRootFileProvider = app.Environment.WebRootFileProvider;
        IFileInfo fallbackFile = webRootFileProvider.GetFileInfo(SPA_FALLBACK_FILE.TrimStart('/'));
        return fallbackFile.Exists;
    }
}
