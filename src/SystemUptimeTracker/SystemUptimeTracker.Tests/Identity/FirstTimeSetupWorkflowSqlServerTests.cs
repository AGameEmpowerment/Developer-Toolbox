using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Api.Extensions;
using SystemUptimeTracker.Api.Models.Identity;
using SystemUptimeTracker.Api.Repositories.DependencyInjection;
using SystemUptimeTracker.Data.Identity;
using System.Net;
using System.Net.Http.Json;

namespace SystemUptimeTracker.Tests.Identity;

[NonParallelizable]
[TestFixture(Category = "Integration")]
public sealed class FirstTimeSetupWorkflowSqlServerTests
{
    private const string DEFAULT_TEST_SERVER_CONNECTION_STRING = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True";
    private const string TEST_SERVER_CONNECTION_ENVIRONMENT_VARIABLE = "SystemUptimeTracker__Tests__SqlServer__ConnectionString";

    [Test]
    public async Task FirstTimeSetupWorkflow_EmptySqlServerIdentityStore_CreatesFirstAdminAndDisablesSetup()
    {
        await using FirstTimeSetupSqlServerHost testHost = await CreateFirstTimeSetupSqlServerHostAsync();
        HttpClient anonymousClient = testHost.App.GetTestClient();

        IdentitySetupStatusResponse? initialStatus =
            await anonymousClient.GetFromJsonAsync<IdentitySetupStatusResponse>("/api/identity/setup-status");

        HttpResponseMessage missingTokenResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = "first-admin@example.test",
            Password = "Password1!",
            DisplayName = "First Admin"
        });
        SelfCreateUserResponse? createPayload = await missingTokenResponse.Content.ReadFromJsonAsync<SelfCreateUserResponse>();
        IdentitySetupStatusResponse? completedStatus =
            await anonymousClient.GetFromJsonAsync<IdentitySetupStatusResponse>("/api/identity/setup-status");

        HttpClient cookieClient = CreateCookieTrackingClient(testHost.App);
        HttpResponseMessage loginResponse = await cookieClient.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = "first-admin@example.test",
            Password = "Password1!"
        });

        using IServiceScope scope = testHost.App.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? firstAdmin = await userManager.FindByEmailAsync("first-admin@example.test");

        Assert.Multiple(() =>
        {
            Assert.That(initialStatus, Is.Not.Null);
            Assert.That(initialStatus!.HasUsers, Is.False);
            Assert.That(initialStatus.HasAdministrators, Is.False);
            Assert.That(initialStatus.IsFirstTimeSetup, Is.True);
            Assert.That(initialStatus.CanCreateFirstUser, Is.True);

            Assert.That(missingTokenResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(createPayload, Is.Not.Null);
            Assert.That(createPayload!.IsFirstUser, Is.True);
            Assert.That(createPayload.RequiresRoleAssignment, Is.False);
            Assert.That(createPayload.Roles, Is.EquivalentTo(ApplicationRoleNames.All));

            Assert.That(completedStatus, Is.Not.Null);
            Assert.That(completedStatus!.HasUsers, Is.True);
            Assert.That(completedStatus.HasAdministrators, Is.True);
            Assert.That(completedStatus.IsFirstTimeSetup, Is.False);
            Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            Assert.That(firstAdmin, Is.Not.Null);
            Assert.That(firstAdmin!.EmailConfirmed, Is.True);
            Assert.That(firstAdmin.IsActive, Is.True);
        });

        Assert.That(await context.Users.CountAsync(), Is.EqualTo(1));
        foreach (string roleName in ApplicationRoleNames.All)
        {
            Assert.That(await userManager.IsInRoleAsync(firstAdmin!, roleName), Is.True, $"Expected first-time setup admin to receive role {roleName}.");
        }
    }

    private static async Task<FirstTimeSetupSqlServerHost> CreateFirstTimeSetupSqlServerHostAsync()
    {
        string databaseName = $"SystemUptimeTrackerFirstTimeSetupTests_{Guid.NewGuid():N}";
        string baseConnectionString = ResolveBaseSqlServerConnectionString();
        string databaseConnectionString = BuildDatabaseConnectionString(baseConnectionString, databaseName);

        await CreateDatabaseAsync(baseConnectionString, databaseName);

        WebApplication? app = null;

        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddDataProtection();
            builder.Services.AddAuthorization();
            builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
                .AddIdentityCookies();
            builder.Services.ConfigureApplicationCookie(options =>
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
            });
            builder.Services.AddSingleton<IEmailSender<ApplicationUser>, NoOpEmailSender>();
            builder.RegisterIdentityData((_, options) => options.UseSqlServer(databaseConnectionString));

            app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();

            RouteGroupBuilder identityGroup = app.MapGroup("/api/identity");
            identityGroup.MapSystemUptimeTrackerBootstrapIdentityEndpoints();
            identityGroup.MapIdentityApi<ApplicationUser>();

            using IServiceScope scope = app.Services.CreateScope();
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.MigrateAsync();

            await app.StartAsync();

            return new FirstTimeSetupSqlServerHost(baseConnectionString, databaseName, app);
        }
        catch
        {
            if (app is not null)
            {
                await app.DisposeAsync();
            }

            await DropDatabaseAsync(baseConnectionString, databaseName);
            throw;
        }
    }

    private static HttpClient CreateCookieTrackingClient(WebApplication app)
    {
        HttpClient client = new(new CookieTrackingHandler(app.GetTestServer().CreateHandler()))
        {
            BaseAddress = new Uri("http://localhost")
        };

        return client;
    }

    private static string ResolveBaseSqlServerConnectionString()
    {
        return Environment.GetEnvironmentVariable(TEST_SERVER_CONNECTION_ENVIRONMENT_VARIABLE) ?? DEFAULT_TEST_SERVER_CONNECTION_STRING;
    }

    private static string BuildDatabaseConnectionString(string baseConnectionString, string databaseName)
    {
        var connectionStringBuilder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = databaseName
        };

        return connectionStringBuilder.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string baseConnectionString, string databaseName)
    {
        await using var connection = new SqlConnection(baseConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string baseConnectionString, string databaseName)
    {
        await using var connection = new SqlConnection(baseConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{databaseName}') IS NOT NULL " +
            $"BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FirstTimeSetupSqlServerHost : IAsyncDisposable
    {
        public FirstTimeSetupSqlServerHost(string baseConnectionString, string databaseName, WebApplication app)
        {
            BaseConnectionString = baseConnectionString;
            DatabaseName = databaseName;
            App = app;
        }

        public string BaseConnectionString { get; }

        public string DatabaseName { get; }

        public WebApplication App { get; }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await DropDatabaseAsync(BaseConnectionString, DatabaseName);
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
            foreach (string setCookie in response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
                         ? values
                         : [])
            {
                _cookies.SetCookies(requestUri, setCookie);
            }

            return response;
        }
    }

    private sealed class NoOpEmailSender : IEmailSender<ApplicationUser>
    {
        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
        {
            return Task.CompletedTask;
        }

        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
        {
            return Task.CompletedTask;
        }

        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
        {
            return Task.CompletedTask;
        }
    }
}
