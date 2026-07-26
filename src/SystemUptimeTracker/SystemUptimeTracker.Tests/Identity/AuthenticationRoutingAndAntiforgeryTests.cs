using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SystemUptimeTracker.Api.Authorization;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Api.Extensions;
using SystemUptimeTracker.Api.Helpers;
using SystemUptimeTracker.Api.Helpers.Interfaces;
using SystemUptimeTracker.Api.Helpers.Handlers;
using SystemUptimeTracker.Api.Helpers.Middleware;
using SystemUptimeTracker.Api.Helpers.Web;
using SystemUptimeTracker.Api.Models.Auth;
using SystemUptimeTracker.Api.Models.ApplicationSettings;
using SystemUptimeTracker.Api.Models.Identity;
using SystemUptimeTracker.Api.Repositories.DependencyInjection;
using SystemUptimeTracker.Api.Services.Identity;
using SystemUptimeTracker.Data.Identity;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;

namespace SystemUptimeTracker.Tests.Identity;

[TestFixture(Category = "Integration")]
public sealed class AuthenticationRoutingAndAntiforgeryTests
{
    private const string LOCAL_BEARER_TOKEN = "local-identity-token";
    private const string LOCAL_ADMIN_BEARER_TOKEN = "local-admin-token";
    private const string LOCAL_MANAGER_BEARER_TOKEN = "local-manager-token";
    private const string LOCAL_CONTRIBUTOR_BEARER_TOKEN = "local-contributor-token";
    private const string LOCAL_READ_BEARER_TOKEN = "local-read-token";
    private const string LOCAL_JWT_TOKEN = "header.local.signature";
    [Test]
    public async Task SharedEndpoint_WhenCredentialsUseAnySupportedFamily_ReturnsOk()
    {
        await using WebApplication app = await CreateApplicationAsync();

        HttpClient cookieClient = CreateCookieTrackingClient(app);
        await SignInCookieAsync(cookieClient);

        HttpClient localBearerClient = app.GetTestClient();
        localBearerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_BEARER_TOKEN);

        HttpClient jwtClient = app.GetTestClient();
        jwtClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_JWT_TOKEN);

        HttpResponseMessage cookieResponse = await cookieClient.GetAsync("/test/shared");
        HttpResponseMessage localBearerResponse = await localBearerClient.GetAsync("/test/shared");
        HttpResponseMessage jwtResponse = await jwtClient.GetAsync("/test/shared");

        Assert.Multiple(() =>
        {
            Assert.That(cookieResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(localBearerResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(jwtResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task SchemeEcho_WhenCredentialsHaveDifferentShapes_RoutesToExpectedScheme()
    {
        await using WebApplication app = await CreateApplicationAsync();

        HttpClient cookieClient = CreateCookieTrackingClient(app);
        await SignInCookieAsync(cookieClient);

        HttpClient localBearerClient = app.GetTestClient();
        localBearerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_BEARER_TOKEN);

        HttpClient jwtClient = app.GetTestClient();
        jwtClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_JWT_TOKEN);

        SchemeEchoResponse? cookieEcho = await cookieClient.GetFromJsonAsync<SchemeEchoResponse>("/test/scheme");
        SchemeEchoResponse? localBearerEcho = await localBearerClient.GetFromJsonAsync<SchemeEchoResponse>("/test/scheme");
        SchemeEchoResponse? jwtEcho = await jwtClient.GetFromJsonAsync<SchemeEchoResponse>("/test/scheme");

        Assert.Multiple(() =>
        {
            Assert.That(cookieEcho?.AuthenticationType, Is.EqualTo(IdentityConstants.ApplicationScheme));
            Assert.That(localBearerEcho?.AuthenticationType, Is.EqualTo(IdentityConstants.BearerScheme));
            Assert.That(jwtEcho?.AuthenticationType, Is.EqualTo(JwtBearerDefaults.AuthenticationScheme));
        });
    }

    [Test]
    public async Task SharedEndpoint_WhenAuthenticationIsMissing_ReturnsTraceAwareUnauthorizedProblemDetails()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = app.GetTestClient();

        HttpResponseMessage response = await client.GetAsync("/test/shared");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        ProblemDetails? problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.That(problemDetails, Is.Not.Null);
        Assert.That(problemDetails!.Title, Is.EqualTo("Authentication is required."));
        Assert.That(problemDetails.Detail, Does.Contain("Trace ID:"));
        Assert.That(problemDetails.Extensions, Does.ContainKey("traceId"));
        Assert.That(problemDetails.Extensions, Does.ContainKey("requestId"));
    }

    [Test]
    public async Task UnsafeBrowserWrite_WhenAntiforgeryTokenIsMissing_ReturnsTraceAwareBadRequest()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = CreateCookieTrackingClient(app);
        await SignInCookieAsync(client);

        HttpResponseMessage response = await client.PostAsync(
            "/test/browser-write",
            JsonBody());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        ProblemDetails? problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.That(problemDetails, Is.Not.Null);
        Assert.That(problemDetails!.Title, Is.EqualTo("Antiforgery validation failed."));
        Assert.That(problemDetails.Detail, Does.Contain("Trace ID:"));
        Assert.That(problemDetails.Extensions, Does.ContainKey("traceId"));
        Assert.That(problemDetails.Extensions, Does.ContainKey("requestId"));
    }

    [Test]
    public async Task AntiforgeryTokenEndpoint_WhenRequestTokenCannotBeGenerated_ReturnsInternalServerErrorProblemDetails()
    {
        IAntiforgery antiforgery = Substitute.For<IAntiforgery>();
        antiforgery
            .GetAndStoreTokens(Arg.Any<HttpContext>())
            .Returns(new AntiforgeryTokenSet(
                null,
                "cookie-token",
                "__RequestVerificationToken",
                SystemUptimeTrackerAuthenticationSchemes.ANTIFORGERY_HEADER_NAME));

        await using WebApplication app = await CreateApplicationAsync(services =>
        {
            services.AddSingleton(antiforgery);
        });
        HttpClient client = CreateCookieTrackingClient(app);
        await SignInCookieAsync(client);

        HttpResponseMessage response = await client.GetAsync("/api/auth/antiforgery-token");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        AssertAntiforgeryTokenResponseIsNotCacheable(response);

        ProblemDetails? problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.That(problemDetails, Is.Not.Null);
        Assert.That(problemDetails!.Title, Is.EqualTo("Antiforgery token generation failed."));
        Assert.That(problemDetails.Detail, Does.Contain("Trace ID:"));
        Assert.That(problemDetails.Extensions, Does.ContainKey("traceId"));
        Assert.That(problemDetails.Extensions, Does.ContainKey("requestId"));
    }

    [Test]
    public async Task AntiforgeryTokenEndpoint_WhenRuntimeHeaderNameDiffers_ReturnsTokenSetHeaderName()
    {
        IAntiforgery antiforgery = Substitute.For<IAntiforgery>();
        antiforgery
            .GetAndStoreTokens(Arg.Any<HttpContext>())
            .Returns(new AntiforgeryTokenSet(
                "request-token",
                "cookie-token",
                "__RequestVerificationToken",
                "X-RUNTIME-CSRF"));

        await using WebApplication app = await CreateApplicationAsync(services =>
        {
            services.AddSingleton(antiforgery);
        });
        HttpClient client = CreateCookieTrackingClient(app);
        await SignInCookieAsync(client);

        AntiforgeryTokenResponse token = await client.GetFromJsonAsync<AntiforgeryTokenResponse>("/api/auth/antiforgery-token")
            ?? throw new InvalidOperationException("The antiforgery endpoint did not return a token response.");

        Assert.Multiple(() =>
        {
            Assert.That(token.RequestToken, Is.EqualTo("request-token"));
            Assert.That(token.HeaderName, Is.EqualTo("X-RUNTIME-CSRF"));
        });
    }

    [Test]
    public async Task UnsafeBrowserWrite_WhenAntiforgeryTokenIsSubmitted_ReturnsOk()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = CreateCookieTrackingClient(app);
        await SignInCookieAsync(client);

        AntiforgeryTokenResponse token = await client.GetFromJsonAsync<AntiforgeryTokenResponse>("/api/auth/antiforgery-token")
            ?? throw new InvalidOperationException("The antiforgery endpoint did not return a token response.");

        using HttpRequestMessage request = new(HttpMethod.Post, "/test/browser-write")
        {
            Content = JsonBody()
        };
        request.Headers.Add(token.HeaderName, token.RequestToken);

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task AntiforgeryTokenEndpoint_WhenTokenIsReturned_DisablesResponseCaching()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = CreateCookieTrackingClient(app);
        await SignInCookieAsync(client);

        HttpResponseMessage response = await client.GetAsync("/api/auth/antiforgery-token");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertAntiforgeryTokenResponseIsNotCacheable(response);
    }

    [Test]
    public async Task UnsafeApiWrite_WhenLocalBearerTokenIsUsed_DoesNotRequireAntiforgeryToken()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_BEARER_TOKEN);

        HttpResponseMessage response = await client.PostAsync(
            "/test/browser-write",
            JsonBody());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task UserManagementPolicy_WhenManagerTokenIsUsed_ReturnsForbidden()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_MANAGER_BEARER_TOKEN);

        HttpResponseMessage response = await client.GetAsync("/test/policies/users");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task UserManagementPolicy_WhenAdminTokenIsUsed_ReturnsOk()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_ADMIN_BEARER_TOKEN);

        HttpResponseMessage response = await client.GetAsync("/test/policies/users");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ProductionUserAdminSurface_WhenAuthenticationIsMissing_ReturnsTraceAwareUnauthorizedProblemDetails()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = app.GetTestClient();

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        ProblemDetails? problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.That(problemDetails, Is.Not.Null);
        Assert.That(problemDetails!.Title, Is.EqualTo("Authentication is required."));
        Assert.That(problemDetails.Detail, Does.Contain("Trace ID:"));
        Assert.That(problemDetails.Extensions, Does.ContainKey("traceId"));
        Assert.That(problemDetails.Extensions, Does.ContainKey("requestId"));
    }

    [Test]
    public async Task ProductionUserAdminSurface_WhenManagerTokenIsUsed_ReturnsForbidden()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_MANAGER_BEARER_TOKEN);

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task ProductionUserRoleUpdateSurface_WhenManagerTokenIsUsed_ReturnsForbidden()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_MANAGER_BEARER_TOKEN);

        HttpResponseMessage response = await client.PutAsJsonAsync("/api/users/user-001/roles", new { Roles = new[] { ApplicationRoleNames.ADMIN } });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task ProductionUserActivationSurface_WhenManagerTokenIsUsed_ReturnsForbidden()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_MANAGER_BEARER_TOKEN);

        HttpResponseMessage response = await client.PutAsJsonAsync("/api/users/user-001/activation", new { IsActive = false });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AuthorizationPermissionsEndpoint_WhenAdminTokenIsUsed_ReturnsAllowedCapabilities()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_ADMIN_BEARER_TOKEN);

        AuthorizationPoliciesResponse? response = await client.GetFromJsonAsync<AuthorizationPoliciesResponse>("/api/auth/permissions");

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.CanManageUsers, Is.True);
    }

    [Test]
    public async Task AuthorizationPermissionsEndpoint_WhenAdminTokenIsUsed_DisablesResponseCaching()
    {
        await using WebApplication app = await CreateApplicationAsync();
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_ADMIN_BEARER_TOKEN);

        HttpResponseMessage response = await client.GetAsync("/api/auth/permissions");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertAntiforgeryTokenResponseIsNotCacheable(response);
    }

    [Test]
    public async Task AuthorizationPermissionsEndpoint_WhenJwtUserRolesChange_UsesIdentityStoreRolesOnNextRequest()
    {
        const string DATABASE_NAME = "SystemUptimeTrackerJwtUserRoles";

        await using WebApplication app = await CreateApplicationAsync(
            configureServices: null,
            configureBuilder: builder =>
            {
                builder.RegisterIdentityData((_, options) => options.UseInMemoryDatabase(DATABASE_NAME));
            });

        using (IServiceScope scope = app.Services.CreateScope())
        {
            IIdentityRoleSeeder roleSeeder = scope.ServiceProvider.GetRequiredService<IIdentityRoleSeeder>();
            await roleSeeder.EnsureSeedDataAsync();

            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var jwtUser = new ApplicationUser
            {
                Id = "local-jwt-user",
                UserName = "jwt-user@example.test",
                Email = "jwt-user@example.test",
                DisplayName = "JWT User",
                IsActive = true,
                EmailConfirmed = true
            };

            IdentityResult createResult = await userManager.CreateAsync(jwtUser);
            Assert.That(createResult.Succeeded, Is.True, string.Join("; ", createResult.Errors.Select(error => error.Description)));

            IList<string> bootstrapRoles = await userManager.GetRolesAsync(jwtUser);
            if (bootstrapRoles.Count > 0)
            {
                IdentityResult removeRolesResult = await userManager.RemoveFromRolesAsync(jwtUser, bootstrapRoles);
                Assert.That(removeRolesResult.Succeeded, Is.True, string.Join("; ", removeRolesResult.Errors.Select(error => error.Description)));
            }
        }

        HttpClient jwtClient = app.GetTestClient();
        jwtClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_JWT_TOKEN);

        AuthorizationPoliciesResponse beforeUpdate = await jwtClient.GetFromJsonAsync<AuthorizationPoliciesResponse>("/api/auth/permissions")
            ?? throw new AssertionException("Expected permissions payload before update.");
        Assert.That(beforeUpdate.CanManageUsers, Is.False);

        HttpClient adminClient = app.GetTestClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_ADMIN_BEARER_TOKEN);

        HttpResponseMessage roleUpdateResponse = await adminClient.PutAsJsonAsync("/api/users/local-jwt-user/roles", new UpdateUserRolesRequest
        {
            Roles = [ApplicationRoleNames.ADMIN]
        });

        Assert.That(roleUpdateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        AuthorizationPoliciesResponse afterUpdate = await jwtClient.GetFromJsonAsync<AuthorizationPoliciesResponse>("/api/auth/permissions")
            ?? throw new AssertionException("Expected permissions payload after update.");
        Assert.That(afterUpdate.CanManageUsers, Is.True);
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        Action<IServiceCollection>? configureServices = null,
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var appSettings = new AppSettings();
        appSettings.Auth.Jwt.Enabled = true;

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        configureBuilder?.Invoke(builder);
        builder.Services.AddRouting();
        builder.Services.AddDataProtection();
        builder.Services.AddOptions<AppSettings>().Configure(options =>
        {
            options.Auth.Jwt.Enabled = true;
        });
        builder.Services.AddScoped<IControllerDependencyBundle, ControllerDependencyBundle>();
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = SystemUptimeTrackerAuthenticationSchemes.ANTIFORGERY_HEADER_NAME;
        });
        builder.Services.AddProblemDetails();
        builder.Services.AddControllers();
        builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationMiddlewareResultHandler>();
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SystemUptimeTrackerAuthenticationSchemes.APPLICATION;
                options.DefaultChallengeScheme = SystemUptimeTrackerAuthenticationSchemes.APPLICATION;
                options.DefaultScheme = SystemUptimeTrackerAuthenticationSchemes.APPLICATION;
            })
            .AddPolicyScheme(SystemUptimeTrackerAuthenticationSchemes.APPLICATION, "SystemUptimeTracker test authentication", options =>
            {
                options.ForwardDefaultSelector = context => SystemUptimeTrackerAuthenticationSchemeSelector.Resolve(context, jwtEnabled: true);
            })
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                };
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerAuthenticationHandler>(
                IdentityConstants.BearerScheme,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, TestBearerAuthenticationHandler>(
                JwtBearerDefaults.AuthenticationScheme,
                _ => { });

        builder.Services.AddAuthorization(SystemUptimeTrackerAuthorizationPolicyCatalog.Configure);
        builder.Services.AddTransient<IClaimsTransformation, IdentityStoreClaimsTransformation>();

        configureServices?.Invoke(builder.Services);

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseMiddleware<CookieAuthenticatedAntiforgeryMiddleware>();
        app.UseAuthorization();
        app.MapSystemUptimeTrackerAntiforgeryEndpoints();
        app.MapControllers();
        app.MapSystemUptimeTrackerProtectedApplicationEndpoints();
        MapTestEndpoints(app);

        await app.StartAsync();
        return app;
    }

    private static void MapTestEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/test/sign-in-cookie", async (HttpContext httpContext) =>
            {
                var identity = new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "local-cookie-user")],
                    IdentityConstants.ApplicationScheme);

                await httpContext.SignInAsync(
                    IdentityConstants.ApplicationScheme,
                    new ClaimsPrincipal(identity));

                return Results.NoContent();
            })
            .AllowAnonymous();

        endpoints.MapGet("/test/shared", () => Results.Ok())
            .RequireAuthorization(AuthorizationPolicyNames.AUTHENTICATED_USER);

        endpoints.MapGet("/test/scheme", (ClaimsPrincipal user) =>
                Results.Ok(new SchemeEchoResponse(user.Identity?.AuthenticationType ?? string.Empty)))
            .RequireAuthorization(AuthorizationPolicyNames.AUTHENTICATED_USER);

        endpoints.MapPost("/test/browser-write", () => Results.Ok())
            .RequireAuthorization(AuthorizationPolicyNames.AUTHENTICATED_USER);

        endpoints.MapGet("/test/policies/users", () => Results.Ok())
            .RequireUserManagementPolicy();
    }

    private static async Task SignInCookieAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsync("/test/sign-in-cookie", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    private static StringContent JsonBody()
    {
        return new StringContent("{}", Encoding.UTF8, "application/json");
    }

    private static void AssertAntiforgeryTokenResponseIsNotCacheable(HttpResponseMessage response)
    {
        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(response.Headers.CacheControl?.NoCache, Is.True);
            Assert.That(response.Headers.Pragma.Select(value => value.Name), Does.Contain("no-cache"));
            Assert.That(response.Content.Headers.Expires, Is.EqualTo(DateTimeOffset.UnixEpoch));
        });
    }

    private static HttpClient CreateCookieTrackingClient(WebApplication app)
    {
        HttpClient client = new(new CookieTrackingHandler(app.GetTestServer().CreateHandler()))
        {
            BaseAddress = new Uri("http://localhost")
        };

        return client;
    }

    private sealed record SchemeEchoResponse(string AuthenticationType);

    private sealed class TestBearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestBearerAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string authorizationHeader = Request.Headers.Authorization.ToString();
            if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            string token = authorizationHeader["Bearer ".Length..].Trim();
            ClaimsPrincipal? principal = ResolvePrincipal(token);

            if (principal is null)
            {
                return Task.FromResult(AuthenticateResult.Fail("Unsupported test token."));
            }

            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        private ClaimsPrincipal? ResolvePrincipal(string token)
        {
            if (string.Equals(Scheme.Name, IdentityConstants.BearerScheme, StringComparison.Ordinal) &&
                string.Equals(token, LOCAL_BEARER_TOKEN, StringComparison.Ordinal))
            {
                return CreatePrincipal(IdentityConstants.BearerScheme, [new Claim(ClaimTypes.NameIdentifier, "local-bearer-user")]);
            }

            if (string.Equals(Scheme.Name, IdentityConstants.BearerScheme, StringComparison.Ordinal) &&
                string.Equals(token, LOCAL_ADMIN_BEARER_TOKEN, StringComparison.Ordinal))
            {
                return CreateLocalRolePrincipal("local-admin-user", ApplicationRoleNames.ADMIN);
            }

            if (string.Equals(Scheme.Name, IdentityConstants.BearerScheme, StringComparison.Ordinal) &&
                string.Equals(token, LOCAL_MANAGER_BEARER_TOKEN, StringComparison.Ordinal))
            {
                return CreateLocalRolePrincipal("local-manager-user", ApplicationRoleNames.MANAGER);
            }

            if (string.Equals(Scheme.Name, IdentityConstants.BearerScheme, StringComparison.Ordinal) &&
                string.Equals(token, LOCAL_CONTRIBUTOR_BEARER_TOKEN, StringComparison.Ordinal))
            {
                return CreateLocalRolePrincipal("local-contributor-user", ApplicationRoleNames.CONTRIBUTOR);
            }

            if (string.Equals(Scheme.Name, IdentityConstants.BearerScheme, StringComparison.Ordinal) &&
                string.Equals(token, LOCAL_READ_BEARER_TOKEN, StringComparison.Ordinal))
            {
                return CreateLocalRolePrincipal("local-read-user", ApplicationRoleNames.READ);
            }

            if (string.Equals(Scheme.Name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal) &&
                string.Equals(token, LOCAL_JWT_TOKEN, StringComparison.Ordinal))
            {
                return CreatePrincipal(JwtBearerDefaults.AuthenticationScheme,
                [
                    new Claim("sub", "local-jwt-user")
                ]);
            }

            return null;
        }

        private static ClaimsPrincipal CreateLocalRolePrincipal(string accountId, string role)
        {
            return CreatePrincipal(
                IdentityConstants.BearerScheme,
                [
                    new Claim(ClaimTypes.NameIdentifier, accountId),
                    new Claim(ClaimTypes.Role, role)
                ]);
        }

        private static ClaimsPrincipal CreatePrincipal(string authenticationType, IEnumerable<Claim> claims)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
        }
    }

    private sealed class CookieTrackingHandler : DelegatingHandler
    {
        private readonly CookieContainer _cookies = new();

        public CookieTrackingHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri requestUri = request.RequestUri ?? new Uri("http://localhost");
            string cookieHeader = _cookies.GetCookieHeader(requestUri);

            if (!string.IsNullOrWhiteSpace(cookieHeader))
            {
                request.Headers.Remove("Cookie");
                request.Headers.Add("Cookie", cookieHeader);
            }

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookieHeaders))
            {
                foreach (string setCookieHeader in setCookieHeaders)
                {
                    _cookies.SetCookies(requestUri, setCookieHeader);
                }
            }

            return response;
        }
    }

}


