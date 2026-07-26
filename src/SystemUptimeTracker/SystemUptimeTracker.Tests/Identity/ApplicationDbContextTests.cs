using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemUptimeTracker.Api.Authorization;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Api.Extensions;
using SystemUptimeTracker.Api.Helpers.Web;
using SystemUptimeTracker.Api.Models.Auth;
using SystemUptimeTracker.Api.Models.Identity;
using SystemUptimeTracker.Api.Repositories.DependencyInjection;
using SystemUptimeTracker.Api.Services.Identity;
using SystemUptimeTracker.Data.Identity;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace SystemUptimeTracker.Tests.Identity;

[NonParallelizable]
[TestFixture(Category = "Unit")]
public class ApplicationDbContextDesignTimeTests
{
    private const string EXPECTED_MIGRATION_NAME = "20260725213503_InitialCreate";
    private const string SQL_SERVER_PROVIDER_NAME = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string DEFAULT_TEST_SERVER_CONNECTION_STRING = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True";
    private const string DESIGN_TIME_CONNECTION_ENVIRONMENT_VARIABLE = "ConnectionStrings__DefaultConnection";
    private const string TEST_SERVER_CONNECTION_ENVIRONMENT_VARIABLE = "SystemUptimeTracker__Tests__SqlServer__ConnectionString";

    [Test]
    public void DesignTimeApplicationDbContextFactory_WhenInvoked_ReturnsSqlServerContextWithIdentityMigration()
    {
        var factory = new DesignTimeApplicationDbContextFactory();
        string? originalConnectionString = Environment.GetEnvironmentVariable(DESIGN_TIME_CONNECTION_ENVIRONMENT_VARIABLE);

        try
        {
            Environment.SetEnvironmentVariable(DESIGN_TIME_CONNECTION_ENVIRONMENT_VARIABLE, ResolveBaseSqlServerConnectionString());

            using ApplicationDbContext context = factory.CreateDbContext([]);

            Assert.That(context.Database.ProviderName, Is.EqualTo(SQL_SERVER_PROVIDER_NAME));
            Assert.That(context.Database.GetMigrations(), Does.Contain(EXPECTED_MIGRATION_NAME));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DESIGN_TIME_CONNECTION_ENVIRONMENT_VARIABLE, originalConnectionString);
        }
    }

    [Test]
    public void DesignTimeApplicationDbContextFactory_WhenConnectionStringMissing_ThrowsClearConfigurationError()
    {
        var factory = new DesignTimeApplicationDbContextFactory();
        string? originalConnectionString = Environment.GetEnvironmentVariable(DESIGN_TIME_CONNECTION_ENVIRONMENT_VARIABLE);

        try
        {
            Environment.SetEnvironmentVariable(DESIGN_TIME_CONNECTION_ENVIRONMENT_VARIABLE, null);

            Assert.That(
                () => factory.CreateDbContext([]),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains(DESIGN_TIME_CONNECTION_ENVIRONMENT_VARIABLE));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DESIGN_TIME_CONNECTION_ENVIRONMENT_VARIABLE, originalConnectionString);
        }
    }

    private static string ResolveBaseSqlServerConnectionString()
    {
        return Environment.GetEnvironmentVariable(TEST_SERVER_CONNECTION_ENVIRONMENT_VARIABLE) ?? DEFAULT_TEST_SERVER_CONNECTION_STRING;
    }
}

[TestFixture(Category = "Unit")]
public class ApplicationDbContextInMemoryTests
{
    private const string IN_MEMORY_PROVIDER_NAME = "Microsoft.EntityFrameworkCore.InMemory";

    [Test]
    public async Task RegisterIdentityData_InMemoryProvider_SeedsRolesAndBootstrapsFirstUser()
    {
        string databaseName = $"SystemUptimeTrackerIdentityInMemory_{Guid.NewGuid():N}";

        using WebApplication app = CreateIdentityApplication((_, options) => options.UseInMemoryDatabase(databaseName));
        using IServiceScope scope = app.Services.CreateScope();

        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        var user = new ApplicationUser
        {
            UserName = "user@example.test",
            Email = "user@example.test"
        };

        IdentityResult createResult = await userManager.CreateAsync(user, "Password1!");

        Assert.That(createResult.Succeeded, Is.True, string.Join("; ", createResult.Errors.Select(error => error.Description)));
        Assert.That(context.Database.ProviderName, Is.EqualTo(IN_MEMORY_PROVIDER_NAME));
        Assert.That(await context.Users.CountAsync(), Is.EqualTo(1));
        Assert.That(await context.Roles.CountAsync(), Is.EqualTo(ApplicationRoleNames.All.Length));

        foreach (string roleName in ApplicationRoleNames.All)
        {
            Assert.That(await roleManager.RoleExistsAsync(roleName), Is.True, $"Expected seeded role {roleName} to exist.");
            Assert.That(await userManager.IsInRoleAsync(user, roleName), Is.True, $"Expected the first user to be assigned role {roleName}.");
        }
    }

    [Test]
    public async Task RegisterIdentityData_InMemoryProvider_ProductionDoesNotBootstrapFirstUser()
    {
        string databaseName = $"SystemUptimeTrackerIdentityInMemory_{Guid.NewGuid():N}";

        using WebApplication app = CreateIdentityApplication(
            (_, options) => options.UseInMemoryDatabase(databaseName),
            Environments.Production);
        using IServiceScope scope = app.Services.CreateScope();

        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = "public-registrant@example.test",
            Email = "public-registrant@example.test"
        };

        IdentityResult createResult = await userManager.CreateAsync(user, "Password1!");

        Assert.That(createResult.Succeeded, Is.True, string.Join("; ", createResult.Errors.Select(error => error.Description)));
        Assert.That(await context.Users.CountAsync(), Is.EqualTo(1));
        Assert.That(await context.Roles.CountAsync(), Is.EqualTo(ApplicationRoleNames.All.Length));

        foreach (string roleName in ApplicationRoleNames.All)
        {
            Assert.That(await userManager.IsInRoleAsync(user, roleName), Is.False, $"Expected production registration not to grant role {roleName}.");
        }
    }

    [Test]
    public async Task IdentityRoleSeeder_WhenLegacyReadOnlyRoleExists_RenamesRoleToRead()
    {
        string databaseName = $"SystemUptimeTrackerIdentityInMemory_{Guid.NewGuid():N}";

        using WebApplication app = CreateIdentityApplication((_, options) => options.UseInMemoryDatabase(databaseName));
        using IServiceScope scope = app.Services.CreateScope();

        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        IIdentityRoleSeeder roleSeeder = scope.ServiceProvider.GetRequiredService<IIdentityRoleSeeder>();

        IdentityResult createLegacyRoleResult = await roleManager.CreateAsync(new IdentityRole("ReadOnly"));

        Assert.That(createLegacyRoleResult.Succeeded, Is.True, string.Join("; ", createLegacyRoleResult.Errors.Select(error => error.Description)));

        await roleSeeder.EnsureSeedDataAsync();

        bool readRoleExists = await roleManager.RoleExistsAsync(ApplicationRoleNames.READ);
        bool legacyRoleExists = await roleManager.RoleExistsAsync("ReadOnly");

        Assert.Multiple(() =>
        {
            Assert.That(readRoleExists, Is.True);
            Assert.That(legacyRoleExists, Is.False);
        });
    }

    [Test]
    public async Task IdentityRoleSeeder_WhenLegacyReadOnlyAndReadRolesExist_MergesAssignmentsIntoRead()
    {
        string databaseName = $"SystemUptimeTrackerIdentityInMemory_{Guid.NewGuid():N}";

        using WebApplication app = CreateIdentityApplication((_, options) => options.UseInMemoryDatabase(databaseName));
        using IServiceScope scope = app.Services.CreateScope();

        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        IIdentityRoleSeeder roleSeeder = scope.ServiceProvider.GetRequiredService<IIdentityRoleSeeder>();

        IdentityResult createLegacyRoleResult = await roleManager.CreateAsync(new IdentityRole("ReadOnly"));
        IdentityResult createReadRoleResult = await roleManager.CreateAsync(new IdentityRole(ApplicationRoleNames.READ));
        IdentityRole legacyRole = (await roleManager.FindByNameAsync("ReadOnly"))!;
        IdentityRole readRole = (await roleManager.FindByNameAsync(ApplicationRoleNames.READ))!;
        var legacyOnlyUser = new ApplicationUser
        {
            UserName = "legacy-read@example.test",
            Email = "legacy-read@example.test"
        };
        var duplicateRoleUser = new ApplicationUser
        {
            UserName = "duplicate-read@example.test",
            Email = "duplicate-read@example.test"
        };

        context.Users.AddRange(legacyOnlyUser, duplicateRoleUser);
        context.UserRoles.AddRange(
            new IdentityUserRole<string>
            {
                UserId = legacyOnlyUser.Id,
                RoleId = legacyRole.Id
            },
            new IdentityUserRole<string>
            {
                UserId = duplicateRoleUser.Id,
                RoleId = legacyRole.Id
            },
            new IdentityUserRole<string>
            {
                UserId = duplicateRoleUser.Id,
                RoleId = readRole.Id
            });
        await context.SaveChangesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(createLegacyRoleResult.Succeeded, Is.True, string.Join("; ", createLegacyRoleResult.Errors.Select(error => error.Description)));
            Assert.That(createReadRoleResult.Succeeded, Is.True, string.Join("; ", createReadRoleResult.Errors.Select(error => error.Description)));
            Assert.That(legacyRole, Is.Not.Null);
            Assert.That(readRole, Is.Not.Null);
        });

        await roleSeeder.EnsureSeedDataAsync();

        bool readRoleExists = await roleManager.RoleExistsAsync(ApplicationRoleNames.READ);
        bool legacyRoleExists = await roleManager.RoleExistsAsync("ReadOnly");
        bool legacyOnlyUserHasRead = await userManager.IsInRoleAsync(legacyOnlyUser, ApplicationRoleNames.READ);
        bool duplicateRoleUserHasRead = await userManager.IsInRoleAsync(duplicateRoleUser, ApplicationRoleNames.READ);
        int duplicateRoleAssignmentCount = await context.UserRoles.CountAsync(userRole => userRole.UserId == duplicateRoleUser.Id);

        Assert.Multiple(() =>
        {
            Assert.That(readRoleExists, Is.True);
            Assert.That(legacyRoleExists, Is.False);
            Assert.That(legacyOnlyUserHasRead, Is.True);
            Assert.That(duplicateRoleUserHasRead, Is.True);
            Assert.That(duplicateRoleAssignmentCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RegisterIdentityData_InMemoryProvider_EnforcesRequireUniqueEmailOption()
    {
        string databaseName = $"SystemUptimeTrackerIdentityInMemory_{Guid.NewGuid():N}";

        using WebApplication app = CreateIdentityApplication((_, options) => options.UseInMemoryDatabase(databaseName));
        using IServiceScope scope = app.Services.CreateScope();

        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var firstUser = new ApplicationUser
        {
            UserName = "user-one@example.test",
            Email = "duplicate@example.test"
        };
        var secondUser = new ApplicationUser
        {
            UserName = "user-two@example.test",
            Email = "duplicate@example.test"
        };

        IdentityResult firstResult = await userManager.CreateAsync(firstUser, "Password1!");
        IdentityResult secondResult = await userManager.CreateAsync(secondUser, "Password1!");

        Assert.That(firstResult.Succeeded, Is.True, string.Join("; ", firstResult.Errors.Select(error => error.Description)));
        Assert.That(secondResult.Succeeded, Is.False);
        Assert.That(secondResult.Errors.Select(error => error.Code), Does.Contain("DuplicateEmail"));
    }

    private static WebApplication CreateIdentityApplication(
        Action<IServiceProvider, DbContextOptionsBuilder> configureDbContext,
        string? environmentName = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName ?? Environments.Development
        });

        builder.Services.AddDataProtection();
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SystemUptimeTrackerAuthenticationSchemes.APPLICATION;
                options.DefaultChallengeScheme = SystemUptimeTrackerAuthenticationSchemes.APPLICATION;
                options.DefaultScheme = SystemUptimeTrackerAuthenticationSchemes.APPLICATION;
            })
            .AddPolicyScheme(SystemUptimeTrackerAuthenticationSchemes.APPLICATION, "SystemUptimeTracker test authentication", options =>
            {
                options.ForwardDefaultSelector = context => SystemUptimeTrackerAuthenticationSchemeSelector.Resolve(context);
            })
            .AddIdentityCookies();

        builder.RegisterIdentityData(configureDbContext);

        return builder.Build();
    }
}

[TestFixture(Category = "Integration")]
public class IdentityApiInMemoryTests
{
    private const string LOCAL_ADMIN_JWT_TOKEN = "header.local-admin.signature";
    private const string LOCAL_ADMIN_JWT_USER_ID = "local-jwt-admin";
    private const string LOCAL_ADMIN_JWT_EMAIL = "local-jwt-admin@example.test";

    [Test]
    public async Task IdentityApi_Register_FirstLocalUserGetsAllSeededRoles()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient client = app.GetTestClient();
        string email = "bootstrap@example.test";

        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/identity/register", new
        {
            Email = email,
            Password = "Password1!"
        });

        Assert.That(registerResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using IServiceScope scope = app.Services.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? createdUser = await userManager.FindByEmailAsync(email);

        Assert.That(createdUser, Is.Not.Null);

        foreach (string roleName in ApplicationRoleNames.All)
        {
            Assert.That(await userManager.IsInRoleAsync(createdUser!, roleName), Is.True, $"Expected the registered first user to receive role {roleName}.");
        }
    }

    [Test]
    public async Task IdentityApi_BootstrapAdmin_WhenNoUsersExist_CreatesConfirmedAdminWithAllRoles()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient client = app.GetTestClient();
        string email = "owner@example.test";

        HttpResponseMessage bootstrapResponse = await client.PostAsJsonAsync("/api/identity/bootstrap-admin", new
        {
            Email = email,
            Password = "Password1!",
            DisplayName = "Example Owner"
        });

        Assert.That(bootstrapResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        BootstrapAdminUserResponse? response = await bootstrapResponse.Content.ReadFromJsonAsync<BootstrapAdminUserResponse>();
        Assert.That(response, Is.Not.Null);

        using IServiceScope scope = app.Services.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? createdUser = await userManager.FindByEmailAsync(email);

        Assert.Multiple(() =>
        {
            Assert.That(response!.Email, Is.EqualTo(email));
            Assert.That(response.DisplayName, Is.EqualTo("Example Owner"));
            Assert.That(response.Roles, Is.EquivalentTo(ApplicationRoleNames.All));
            Assert.That(createdUser, Is.Not.Null);
            Assert.That(createdUser!.EmailConfirmed, Is.True);
        });

        foreach (string roleName in ApplicationRoleNames.All)
        {
            Assert.That(await userManager.IsInRoleAsync(createdUser!, roleName), Is.True, $"Expected bootstrap admin to receive role {roleName}.");
        }
    }

    [Test]
    public async Task IdentityApi_BootstrapAdmin_WhenProductionHasNoAdministrator_CreatesConfirmedAdminWithAllRoles()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(
            databaseName,
            Environments.Production);
        HttpClient client = app.GetTestClient();

        HttpResponseMessage bootstrapResponse = await client.PostAsJsonAsync("/api/identity/bootstrap-admin", new
        {
            Email = "owner@example.test",
            Password = "Password1!"
        });

        Assert.That(bootstrapResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        BootstrapAdminUserResponse? response = await bootstrapResponse.Content.ReadFromJsonAsync<BootstrapAdminUserResponse>();

        using IServiceScope scope = app.Services.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? createdUser = await userManager.FindByEmailAsync("owner@example.test");

        Assert.Multiple(() =>
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Roles, Is.EquivalentTo(ApplicationRoleNames.All));
            Assert.That(createdUser, Is.Not.Null);
            Assert.That(createdUser!.EmailConfirmed, Is.True);
        });
    }

    [Test]
    public async Task IdentityApi_BootstrapAdmin_WhenActiveAdministratorExists_ReturnsConflict()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(
            databaseName,
            Environments.Production);
        HttpClient client = app.GetTestClient();

        HttpResponseMessage firstBootstrapResponse = await client.PostAsJsonAsync("/api/identity/bootstrap-admin", new
        {
            Email = "owner@example.test",
            Password = "Password1!"
        });
        Assert.That(firstBootstrapResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage bootstrapResponse = await client.PostAsJsonAsync("/api/identity/bootstrap-admin", new
        {
            Email = "second-owner@example.test",
            Password = "Password1!"
        });

        Assert.That(bootstrapResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        using IServiceScope scope = app.Services.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? unexpectedUser = await userManager.FindByEmailAsync("second-owner@example.test");

        Assert.That(unexpectedUser, Is.Null, "Bootstrap conflicts should not leave behind a partially created user.");
    }

    [Test]
    public async Task IdentityApi_BootstrapAdmin_WhenRequestIsMissingRequiredFields_ReturnsValidationProblem()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient client = app.GetTestClient();

        HttpResponseMessage bootstrapResponse = await client.PostAsJsonAsync("/api/identity/bootstrap-admin", new { });

        Assert.That(bootstrapResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task IdentityApi_SelfCreate_WhenRequestIsMissingRequiredFields_ReturnsValidationProblem()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient client = app.GetTestClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/identity/self-create", new { });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task IdentityApi_SelfCreate_WhenProductionFirstUserDoesNotRequireAdditionalSetupToken_CreatesFirstUserWithAllRoles()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(
            databaseName,
            Environments.Production);
        HttpClient client = app.GetTestClient();
        string email = "first-self-created@example.test";

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = email,
            Password = "Password1!",
            DisplayName = "First Self Created"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        SelfCreateUserResponse? payload = await response.Content.ReadFromJsonAsync<SelfCreateUserResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.IsFirstUser, Is.True);
            Assert.That(payload.RequiresRoleAssignment, Is.False);
            Assert.That(payload.Roles, Is.EquivalentTo(ApplicationRoleNames.All));
        });
    }

    [Test]
    public async Task IdentityApi_SelfCreate_WhenProductionUsersExistButNoAdmin_CreatesAdministratorWithAllRoles()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(
            databaseName,
            Environments.Production);
        HttpClient client = app.GetTestClient();

        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            IdentityResult createResult = await userManager.CreateAsync(new ApplicationUser
            {
                UserName = "existing@example.test",
                Email = "existing@example.test",
                EmailConfirmed = true,
                DisplayName = "Existing User",
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            }, "Password1!");

            Assert.That(createResult.Succeeded, Is.True);
        }

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = "replacement-admin@example.test",
            Password = "Password1!",
            DisplayName = "Replacement Admin"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        SelfCreateUserResponse? payload = await response.Content.ReadFromJsonAsync<SelfCreateUserResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.IsFirstUser, Is.True);
            Assert.That(payload.RequiresRoleAssignment, Is.False);
            Assert.That(payload.Roles, Is.EquivalentTo(ApplicationRoleNames.All));
        });
    }

    [Test]
    public async Task IdentityApi_SelfCreate_WhenProductionUsersAlreadyExist_CreatesUserWithoutRoles()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(
            databaseName,
            Environments.Production);
        HttpClient client = app.GetTestClient();

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = "first-self-created@example.test",
            Password = "Password1!"
        });
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        string secondEmail = "pending-approval@example.test";
        HttpResponseMessage secondResponse = await client.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = secondEmail,
            Password = "Password1!",
            DisplayName = "Pending Approval"
        });

        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        SelfCreateUserResponse? payload = await secondResponse.Content.ReadFromJsonAsync<SelfCreateUserResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.Email, Is.EqualTo(secondEmail));
            Assert.That(payload.IsFirstUser, Is.False);
            Assert.That(payload.RequiresRoleAssignment, Is.True);
            Assert.That(payload.Roles, Is.Empty);
        });
    }

    [Test]
    public async Task IdentityApi_Register_WhenProductionFirstUserCreated_DoesNotGrantAdminRoles()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(
            databaseName,
            Environments.Production);
        HttpClient client = app.GetTestClient();
        string email = "public-registrant@example.test";

        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/identity/register", new
        {
            Email = email,
            Password = "Password1!"
        });

        Assert.That(registerResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using IServiceScope scope = app.Services.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? createdUser = await userManager.FindByEmailAsync(email);

        Assert.That(createdUser, Is.Not.Null);
        foreach (string roleName in ApplicationRoleNames.All)
        {
            Assert.That(await userManager.IsInRoleAsync(createdUser!, roleName), Is.False, $"Expected production registration not to grant role {roleName}.");
        }
    }

    [Test]
    public async Task IdentityApi_BootstrapAdmin_WhenAUserAlreadyExists_ReturnsConflict()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient client = app.GetTestClient();

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync("/api/identity/bootstrap-admin", new
        {
            Email = "owner@example.test",
            Password = "Password1!"
        });
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage secondResponse = await client.PostAsJsonAsync("/api/identity/bootstrap-admin", new
        {
            Email = "second@example.test",
            Password = "Password1!"
        });

        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task IdentityApi_SelfCreate_WhenNoUsersExist_CreatesFirstUserWithAllRoles()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient client = app.GetTestClient();
        string email = "first-self-created@example.test";

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = email,
            Password = "Password1!",
            DisplayName = "First Self Created"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        SelfCreateUserResponse? payload = await response.Content.ReadFromJsonAsync<SelfCreateUserResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.IsFirstUser, Is.True);
            Assert.That(payload.RequiresRoleAssignment, Is.False);
            Assert.That(payload.Roles, Is.EquivalentTo(ApplicationRoleNames.All));
        });
    }

    [Test]
    public async Task IdentityApi_SelfCreate_WhenUsersAlreadyExist_CreatesUserWithoutRoles()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient client = app.GetTestClient();

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = "first-self-created@example.test",
            Password = "Password1!"
        });
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        string secondEmail = "pending-approval@example.test";
        HttpResponseMessage secondResponse = await client.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = secondEmail,
            Password = "Password1!",
            DisplayName = "Pending Approval"
        });

        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        SelfCreateUserResponse? payload = await secondResponse.Content.ReadFromJsonAsync<SelfCreateUserResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.Email, Is.EqualTo(secondEmail));
            Assert.That(payload.IsFirstUser, Is.False);
            Assert.That(payload.RequiresRoleAssignment, Is.True);
            Assert.That(payload.Roles, Is.Empty);
        });

        using IServiceScope scope = app.Services.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? secondUser = await userManager.FindByEmailAsync(secondEmail);

        Assert.That(secondUser, Is.Not.Null);
        foreach (string roleName in ApplicationRoleNames.All)
        {
            Assert.That(await userManager.IsInRoleAsync(secondUser!, roleName), Is.False, $"Expected self-created user not to receive role {roleName}.");
        }
    }

    [Test]
    public async Task UserManagementApi_WhenRoleRequestOmitsRoles_ReturnsValidationProblem()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient anonymousClient = app.GetTestClient();
        HttpClient adminClient = CreateCookieTrackingClient(app);
        const string ADMIN_EMAIL = "admin@example.test";
        const string ADMIN_PASSWORD = "Password1!";
        const string PENDING_EMAIL = "pending@example.test";

        HttpResponseMessage adminCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage pendingCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = PENDING_EMAIL,
            Password = "Password1!"
        });
        Assert.That(pendingCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage loginResponse = await adminClient.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        HttpResponseMessage usersResponse = await adminClient.GetAsync("/api/users");
        Assert.That(usersResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        UserManagementSurfaceSummary[]? users = await usersResponse.Content.ReadFromJsonAsync<UserManagementSurfaceSummary[]>();
        UserManagementSurfaceSummary pendingUser = users!.Single(user => user.Email == PENDING_EMAIL);

        HttpResponseMessage roleUpdateResponse = await adminClient.PutAsJsonAsync($"/api/users/{pendingUser.UserId}/roles", new
        {
            Roles = (string[]?)null
        });

        Assert.That(roleUpdateResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task UserManagementApi_WhenActivationRequestOmitsIsActive_ReturnsValidationProblem()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient anonymousClient = app.GetTestClient();
        HttpClient adminClient = CreateCookieTrackingClient(app);
        const string ADMIN_EMAIL = "admin@example.test";
        const string ADMIN_PASSWORD = "Password1!";
        const string PENDING_EMAIL = "pending@example.test";

        HttpResponseMessage adminCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage pendingCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = PENDING_EMAIL,
            Password = "Password1!"
        });
        Assert.That(pendingCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage loginResponse = await adminClient.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        UserManagementSurfaceSummary[]? users = await adminClient.GetFromJsonAsync<UserManagementSurfaceSummary[]>("/api/users");
        UserManagementSurfaceSummary pendingUser = users!.Single(user => user.Email == PENDING_EMAIL);

        HttpResponseMessage activationResponse = await adminClient.PutAsJsonAsync($"/api/users/{pendingUser.UserId}/activation", new { });

        Assert.That(activationResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task UserManagementApi_WhenAdminAssignsRole_GrantsSelfCreatedUserAccessRole()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient anonymousClient = app.GetTestClient();
        HttpClient adminClient = CreateCookieTrackingClient(app);
        const string ADMIN_EMAIL = "admin@example.test";
        const string ADMIN_PASSWORD = "Password1!";
        const string PENDING_EMAIL = "pending@example.test";

        HttpResponseMessage adminCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage pendingCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = PENDING_EMAIL,
            Password = "Password1!"
        });
        Assert.That(pendingCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage loginResponse = await adminClient.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        HttpResponseMessage usersResponse = await adminClient.GetAsync("/api/users");
        Assert.That(usersResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        UserManagementSurfaceSummary[]? users = await usersResponse.Content.ReadFromJsonAsync<UserManagementSurfaceSummary[]>();
        UserManagementSurfaceSummary pendingUser = users!.Single(user => user.Email == PENDING_EMAIL);
        Assert.That(pendingUser.Roles, Is.Empty);

        HttpResponseMessage roleUpdateResponse = await adminClient.PutAsJsonAsync($"/api/users/{pendingUser.UserId}/roles", new UpdateUserRolesRequest
        {
            Roles = [ApplicationRoleNames.CONTRIBUTOR]
        });

        Assert.That(roleUpdateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        UserManagementSurfaceSummary? updatedUser = await roleUpdateResponse.Content.ReadFromJsonAsync<UserManagementSurfaceSummary>();

        Assert.Multiple(() =>
        {
            Assert.That(updatedUser, Is.Not.Null);
            Assert.That(updatedUser!.Email, Is.EqualTo(PENDING_EMAIL));
            Assert.That(updatedUser.Roles, Is.EquivalentTo(new[] { ApplicationRoleNames.CONTRIBUTOR }));
        });
    }

    [Test]
    public async Task UserManagementApi_WhenAdminAssignsRole_RefreshesExistingUserPermissionsOnNextRequest()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient anonymousClient = app.GetTestClient();
        HttpClient adminClient = CreateCookieTrackingClient(app);
        HttpClient memberClient = CreateCookieTrackingClient(app);
        const string ADMIN_EMAIL = "admin@example.test";
        const string ADMIN_PASSWORD = "Password1!";
        const string MEMBER_EMAIL = "member@example.test";
        const string MEMBER_PASSWORD = "Password1!";

        HttpResponseMessage adminCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage memberCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = MEMBER_EMAIL,
            Password = MEMBER_PASSWORD
        });
        Assert.That(memberCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage memberLoginResponse = await memberClient.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = MEMBER_EMAIL,
            Password = MEMBER_PASSWORD
        });
        Assert.That(memberLoginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        AuthorizationPoliciesResponse? beforeAssignment = await memberClient.GetFromJsonAsync<AuthorizationPoliciesResponse>("/api/auth/permissions");
        Assert.That(beforeAssignment, Is.Not.Null);
        Assert.That(beforeAssignment!.CanManageUsers, Is.False);

        HttpResponseMessage adminLoginResponse = await adminClient.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminLoginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        UserManagementSurfaceSummary[]? users = await adminClient.GetFromJsonAsync<UserManagementSurfaceSummary[]>("/api/users");
        UserManagementSurfaceSummary memberUser = users!.Single(user => user.Email == MEMBER_EMAIL);

        HttpResponseMessage roleUpdateResponse = await adminClient.PutAsJsonAsync($"/api/users/{memberUser.UserId}/roles", new UpdateUserRolesRequest
        {
            Roles = [ApplicationRoleNames.ADMIN]
        });
        Assert.That(roleUpdateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            SignInManager<ApplicationUser> signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
            IAuthorizationService authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

            ApplicationUser refreshedUser = await userManager.FindByIdAsync(memberUser.UserId)
                ?? throw new AssertionException("Expected refreshed member user.");
            IList<string> refreshedRoles = await userManager.GetRolesAsync(refreshedUser);
            Assert.That(refreshedRoles, Is.EquivalentTo(new[] { ApplicationRoleNames.ADMIN }));

            ClaimsPrincipal refreshedPrincipal = await signInManager.CreateUserPrincipalAsync(refreshedUser);
            AuthorizationResult manageUsersResult = await authorizationService.AuthorizeAsync(
                refreshedPrincipal,
                AuthorizationPolicyNames.CAN_MANAGE_USERS);

            Assert.That(manageUsersResult.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task UserManagementApi_WhenRoleUpdateIsNoOp_DoesNotRefreshSecurityStamp()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient anonymousClient = app.GetTestClient();
        HttpClient adminClient = CreateCookieTrackingClient(app);
        const string ADMIN_EMAIL = "admin@example.test";
        const string ADMIN_PASSWORD = "Password1!";
        const string MEMBER_EMAIL = "member@example.test";

        HttpResponseMessage adminCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage memberCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = MEMBER_EMAIL,
            Password = "Password1!"
        });
        Assert.That(memberCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage adminLoginResponse = await adminClient.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminLoginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        UserManagementSurfaceSummary[]? users = await adminClient.GetFromJsonAsync<UserManagementSurfaceSummary[]>("/api/users");
        UserManagementSurfaceSummary memberUser = users!.Single(user => user.Email == MEMBER_EMAIL);

        HttpResponseMessage initialRoleUpdateResponse = await adminClient.PutAsJsonAsync($"/api/users/{memberUser.UserId}/roles", new UpdateUserRolesRequest
        {
            Roles = [ApplicationRoleNames.CONTRIBUTOR]
        });
        Assert.That(initialRoleUpdateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        string securityStampAfterInitialUpdate;
        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser refreshedUser = await userManager.FindByIdAsync(memberUser.UserId)
                ?? throw new AssertionException("Expected refreshed member user.");
            securityStampAfterInitialUpdate = refreshedUser.SecurityStamp ?? string.Empty;
        }

        HttpResponseMessage noOpRoleUpdateResponse = await adminClient.PutAsJsonAsync($"/api/users/{memberUser.UserId}/roles", new UpdateUserRolesRequest
        {
            Roles = [ApplicationRoleNames.CONTRIBUTOR]
        });
        Assert.That(noOpRoleUpdateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser refreshedUser = await userManager.FindByIdAsync(memberUser.UserId)
                ?? throw new AssertionException("Expected refreshed member user.");

            Assert.That(refreshedUser.SecurityStamp, Is.EqualTo(securityStampAfterInitialUpdate));
        }
    }

    [Test]
    public async Task UserManagementApi_WhenRoleUpdateChangesAssignments_DoesNotRefreshSecurityStamp()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient anonymousClient = app.GetTestClient();
        HttpClient adminClient = CreateCookieTrackingClient(app);
        const string ADMIN_EMAIL = "admin@example.test";
        const string ADMIN_PASSWORD = "Password1!";
        const string MEMBER_EMAIL = "member@example.test";

        HttpResponseMessage adminCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage memberCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = MEMBER_EMAIL,
            Password = "Password1!"
        });
        Assert.That(memberCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage adminLoginResponse = await adminClient.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminLoginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        UserManagementSurfaceSummary[]? users = await adminClient.GetFromJsonAsync<UserManagementSurfaceSummary[]>("/api/users");
        UserManagementSurfaceSummary memberUser = users!.Single(user => user.Email == MEMBER_EMAIL);

        string originalSecurityStamp;
        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser refreshedUser = await userManager.FindByIdAsync(memberUser.UserId)
                ?? throw new AssertionException("Expected refreshed member user.");
            originalSecurityStamp = refreshedUser.SecurityStamp ?? string.Empty;
        }

        HttpResponseMessage roleUpdateResponse = await adminClient.PutAsJsonAsync($"/api/users/{memberUser.UserId}/roles", new UpdateUserRolesRequest
        {
            Roles = [ApplicationRoleNames.CONTRIBUTOR]
        });
        Assert.That(roleUpdateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser refreshedUser = await userManager.FindByIdAsync(memberUser.UserId)
                ?? throw new AssertionException("Expected refreshed member user.");

            Assert.That(refreshedUser.SecurityStamp, Is.EqualTo(originalSecurityStamp));
        }
    }

    [Test]
    public async Task UserManagementApi_WhenAdminDeactivatesUser_RevokesSessionAndBlocksLaterSignIn()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient anonymousClient = app.GetTestClient();
        HttpClient adminClient = CreateCookieTrackingClient(app);
        HttpClient memberClient = CreateCookieTrackingClient(app);
        HttpClient laterLoginClient = CreateCookieTrackingClient(app);
        const string ADMIN_EMAIL = "admin@example.test";
        const string ADMIN_PASSWORD = "Password1!";
        const string MEMBER_EMAIL = "member@example.test";
        const string MEMBER_PASSWORD = "Password1!";

        HttpResponseMessage adminCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage memberCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = MEMBER_EMAIL,
            Password = MEMBER_PASSWORD
        });
        Assert.That(memberCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage adminLoginResponse = await adminClient.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminLoginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        UserManagementSurfaceSummary[]? users = await adminClient.GetFromJsonAsync<UserManagementSurfaceSummary[]>("/api/users");
        UserManagementSurfaceSummary memberUser = users!.Single(user => user.Email == MEMBER_EMAIL);

        HttpResponseMessage memberLoginResponse = await memberClient.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = MEMBER_EMAIL,
            Password = MEMBER_PASSWORD
        });
        Assert.That(memberLoginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        HttpResponseMessage activationResponse = await adminClient.PutAsJsonAsync($"/api/users/{memberUser.UserId}/activation", new UpdateUserActivationRequest
        {
            IsActive = false
        });
        Assert.That(activationResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            SignInManager<ApplicationUser> signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
            IAuthorizationService authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

            ApplicationUser refreshedUser = await userManager.FindByIdAsync(memberUser.UserId)
                ?? throw new AssertionException("Expected refreshed member user.");

            Assert.That(refreshedUser.IsActive, Is.False);
            Assert.That(await signInManager.CanSignInAsync(refreshedUser), Is.False);

            ClaimsPrincipal refreshedPrincipal = await signInManager.CreateUserPrincipalAsync(refreshedUser);
            AuthorizationResult activeSessionResult = await authorizationService.AuthorizeAsync(
                refreshedPrincipal,
                AuthorizationPolicyNames.AUTHENTICATED_USER);

            Assert.That(activeSessionResult.Succeeded, Is.False);
        }
    }

    [Test]
    public async Task UserManagementApi_WhenAdminDeactivatesSelf_ReturnsValidationProblemAndKeepsAccountActive()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient anonymousClient = app.GetTestClient();
        HttpClient adminClient = CreateCookieTrackingClient(app);
        const string ADMIN_EMAIL = "admin@example.test";
        const string ADMIN_PASSWORD = "Password1!";

        HttpResponseMessage adminCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage adminLoginResponse = await adminClient.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminLoginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        UserManagementSurfaceSummary[]? users = await adminClient.GetFromJsonAsync<UserManagementSurfaceSummary[]>("/api/users");
        UserManagementSurfaceSummary adminUser = users!.Single(user => user.Email == ADMIN_EMAIL);

        HttpResponseMessage activationResponse = await adminClient.PutAsJsonAsync($"/api/users/{adminUser.UserId}/activation", new UpdateUserActivationRequest
        {
            IsActive = false
        });
        Assert.That(activationResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        using IServiceScope scope = app.Services.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser refreshedUser = await userManager.FindByIdAsync(adminUser.UserId)
            ?? throw new AssertionException("Expected refreshed admin user.");

        Assert.That(refreshedUser.IsActive, Is.True);
    }

    [Test]
    public async Task UserManagementApi_WhenJwtAdminDeactivatesOwnAccount_ReturnsValidationProblemAndKeepsAccountActive()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient jwtAdminClient = app.GetTestClient();
        jwtAdminClient.BaseAddress = new Uri("http://localhost");
        jwtAdminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LOCAL_ADMIN_JWT_TOKEN);

        string linkedUserId;
        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var linkedUser = new ApplicationUser
            {
                Id = LOCAL_ADMIN_JWT_USER_ID,
                UserName = LOCAL_ADMIN_JWT_EMAIL,
                Email = LOCAL_ADMIN_JWT_EMAIL,
                DisplayName = "Local JWT Admin",
                EmailConfirmed = true
            };

            IdentityResult createResult = await userManager.CreateAsync(linkedUser);
            Assert.That(createResult.Succeeded, Is.True, string.Join("; ", createResult.Errors.Select(error => error.Description)));

            IList<string> bootstrapRoles = await userManager.GetRolesAsync(linkedUser);
            if (bootstrapRoles.Count > 0)
            {
                IdentityResult removeRolesResult = await userManager.RemoveFromRolesAsync(linkedUser, bootstrapRoles);
                Assert.That(removeRolesResult.Succeeded, Is.True, string.Join("; ", removeRolesResult.Errors.Select(error => error.Description)));
            }

            IdentityResult roleResult = await userManager.AddToRoleAsync(linkedUser, ApplicationRoleNames.ADMIN);
            Assert.That(roleResult.Succeeded, Is.True, string.Join("; ", roleResult.Errors.Select(error => error.Description)));

            linkedUserId = linkedUser.Id;
        }

        HttpResponseMessage activationResponse = await jwtAdminClient.PutAsJsonAsync($"/api/users/{linkedUserId}/activation", new UpdateUserActivationRequest
        {
            IsActive = false
        });
        Assert.That(activationResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        using IServiceScope verificationScope = app.Services.CreateScope();
        UserManager<ApplicationUser> verificationUserManager = verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser refreshedUser = await verificationUserManager.FindByIdAsync(linkedUserId)
            ?? throw new AssertionException("Expected refreshed linked admin user.");

        Assert.That(refreshedUser.IsActive, Is.True);
    }

    [Test]
    public async Task UserManagementApi_WhenAdminUpdatesJwtUser_RoleAndActivationChangesAffectNextAuthorizationCheck()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient anonymousClient = app.GetTestClient();
        HttpClient adminClient = CreateCookieTrackingClient(app);
        const string ADMIN_EMAIL = "admin@example.test";
        const string ADMIN_PASSWORD = "Password1!";
        const string JWT_EMAIL = "jwt-user@example.test";

        HttpResponseMessage adminCreateResponse = await anonymousClient.PostAsJsonAsync("/api/identity/self-create", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminCreateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HttpResponseMessage adminLoginResponse = await adminClient.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = ADMIN_EMAIL,
            Password = ADMIN_PASSWORD
        });
        Assert.That(adminLoginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var linkedUser = new ApplicationUser
            {
                UserName = JWT_EMAIL,
                Email = JWT_EMAIL,
                DisplayName = "JWT User",
                EmailConfirmed = true
            };

            IdentityResult createResult = await userManager.CreateAsync(linkedUser);
            Assert.That(createResult.Succeeded, Is.True, string.Join("; ", createResult.Errors.Select(error => error.Description)));
            foreach (string roleName in ApplicationRoleNames.All)
            {
                Assert.That(await userManager.IsInRoleAsync(linkedUser, roleName), Is.False, $"Expected JWT user not to start with role {roleName}.");
            }
        }

        UserManagementSurfaceSummary[]? users = await adminClient.GetFromJsonAsync<UserManagementSurfaceSummary[]>("/api/users");
        UserManagementSurfaceSummary linkedUserSummary = users!.Single(user => user.Email == JWT_EMAIL);

        using (IServiceScope scope = app.Services.CreateScope())
        {
            IClaimsTransformation claimsTransformation = scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();
            IAuthorizationService authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

            ClaimsPrincipal jwtPrincipal = new(new ClaimsIdentity(
            [
                new Claim("sub", linkedUserSummary.UserId)
            ], JwtBearerDefaults.AuthenticationScheme));

            ClaimsPrincipal beforeRoleAssignment = await claimsTransformation.TransformAsync(jwtPrincipal);
            AuthorizationResult beforeRoleAssignmentResult = await authorizationService.AuthorizeAsync(
                beforeRoleAssignment,
                AuthorizationPolicyNames.CAN_MANAGE_USERS);
            Assert.That(beforeRoleAssignmentResult.Succeeded, Is.False);
        }

        HttpResponseMessage roleUpdateResponse = await adminClient.PutAsJsonAsync($"/api/users/{linkedUserSummary.UserId}/roles", new UpdateUserRolesRequest
        {
            Roles = [ApplicationRoleNames.ADMIN]
        });
        Assert.That(roleUpdateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (IServiceScope scope = app.Services.CreateScope())
        {
            IClaimsTransformation claimsTransformation = scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();
            IAuthorizationService authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

            ClaimsPrincipal jwtPrincipal = new(new ClaimsIdentity(
            [
                new Claim("sub", linkedUserSummary.UserId)
            ], JwtBearerDefaults.AuthenticationScheme));

            ClaimsPrincipal afterRoleAssignment = await claimsTransformation.TransformAsync(jwtPrincipal);
            AuthorizationResult afterRoleAssignmentResult = await authorizationService.AuthorizeAsync(
                afterRoleAssignment,
                AuthorizationPolicyNames.CAN_MANAGE_USERS);
            Assert.That(afterRoleAssignmentResult.Succeeded, Is.True);
        }

        HttpResponseMessage activationResponse = await adminClient.PutAsJsonAsync($"/api/users/{linkedUserSummary.UserId}/activation", new UpdateUserActivationRequest
        {
            IsActive = false
        });
        Assert.That(activationResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (IServiceScope scope = app.Services.CreateScope())
        {
            IClaimsTransformation claimsTransformation = scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();
            IAuthorizationService authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

            ClaimsPrincipal jwtPrincipal = new(new ClaimsIdentity(
            [
                new Claim("sub", linkedUserSummary.UserId)
            ], JwtBearerDefaults.AuthenticationScheme));

            ClaimsPrincipal afterDeactivation = await claimsTransformation.TransformAsync(jwtPrincipal);
            AuthorizationResult afterDeactivationScopeResult = await authorizationService.AuthorizeAsync(
                afterDeactivation,
                AuthorizationPolicyNames.AUTHENTICATED_USER);
            AuthorizationResult afterDeactivationRoleResult = await authorizationService.AuthorizeAsync(
                afterDeactivation,
                AuthorizationPolicyNames.CAN_MANAGE_USERS);

            Assert.Multiple(() =>
            {
                Assert.That(afterDeactivationScopeResult.Succeeded, Is.False);
                Assert.That(afterDeactivationRoleResult.Succeeded, Is.False);
            });
        }
    }

    [Test]
    public async Task IdentityStoreClaimsTransformation_DoesNotMutateInputPrincipal()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);

        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            IClaimsTransformation claimsTransformation = scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();

            var linkedUser = new ApplicationUser
            {
                UserName = "jwt-user@example.test",
                Email = "jwt-user@example.test",
                DisplayName = "JWT User",
                EmailConfirmed = true
            };

            IdentityResult createResult = await userManager.CreateAsync(linkedUser);
            Assert.That(createResult.Succeeded, Is.True, string.Join("; ", createResult.Errors.Select(error => error.Description)));

            IList<string> bootstrapRoles = await userManager.GetRolesAsync(linkedUser);
            if (bootstrapRoles.Count > 0)
            {
                IdentityResult removeRolesResult = await userManager.RemoveFromRolesAsync(linkedUser, bootstrapRoles);
                Assert.That(removeRolesResult.Succeeded, Is.True, string.Join("; ", removeRolesResult.Errors.Select(error => error.Description)));
            }

            IdentityResult roleResult = await userManager.AddToRoleAsync(linkedUser, ApplicationRoleNames.CONTRIBUTOR);
            Assert.That(roleResult.Succeeded, Is.True, string.Join("; ", roleResult.Errors.Select(error => error.Description)));

            ClaimsPrincipal jwtPrincipal = new(new ClaimsIdentity(
            [
                new Claim("sub", linkedUser.Id)
            ], JwtBearerDefaults.AuthenticationScheme));

            ClaimsPrincipal transformedPrincipal = await claimsTransformation.TransformAsync(jwtPrincipal);

            Assert.Multiple(() =>
            {
                Assert.That(jwtPrincipal.FindAll(ClaimTypes.Role), Is.Empty);
                Assert.That(jwtPrincipal.FindAll("roles"), Is.Empty);
                Assert.That(jwtPrincipal.FindFirst(SystemUptimeTrackerClaimTypes.IS_ACTIVE), Is.Null);
                Assert.That(transformedPrincipal.FindAll(ClaimTypes.Role).Select(claim => claim.Value), Is.EquivalentTo(new[] { ApplicationRoleNames.CONTRIBUTOR }));
                Assert.That(transformedPrincipal.FindAll("roles").Select(claim => claim.Value), Is.EquivalentTo(new[] { ApplicationRoleNames.CONTRIBUTOR }));
                Assert.That(transformedPrincipal.FindFirstValue(SystemUptimeTrackerClaimTypes.IS_ACTIVE), Is.EqualTo(bool.TrueString));
            });
        }
    }

    [Test]
    public async Task IdentityStoreClaimsTransformation_RemovesManagedClaimsFromAllClonedIdentities()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);

        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            IClaimsTransformation claimsTransformation = scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();

            var linkedUser = new ApplicationUser
            {
                UserName = "jwt-multi-identity@example.test",
                Email = "jwt-multi-identity@example.test",
                DisplayName = "JWT Multi Identity User",
                EmailConfirmed = true
            };

            IdentityResult createResult = await userManager.CreateAsync(linkedUser);
            Assert.That(createResult.Succeeded, Is.True, string.Join("; ", createResult.Errors.Select(error => error.Description)));

            IList<string> bootstrapRoles = await userManager.GetRolesAsync(linkedUser);
            if (bootstrapRoles.Count > 0)
            {
                IdentityResult removeRolesResult = await userManager.RemoveFromRolesAsync(linkedUser, bootstrapRoles);
                Assert.That(removeRolesResult.Succeeded, Is.True, string.Join("; ", removeRolesResult.Errors.Select(error => error.Description)));
            }

            IdentityResult roleResult = await userManager.AddToRoleAsync(linkedUser, ApplicationRoleNames.CONTRIBUTOR);
            Assert.That(roleResult.Succeeded, Is.True, string.Join("; ", roleResult.Errors.Select(error => error.Description)));

            ClaimsIdentity bearerIdentity = new(
            [
                new Claim("sub", linkedUser.Id)
            ], JwtBearerDefaults.AuthenticationScheme);

            ClaimsIdentity staleIdentity = new(
            [
                new Claim(ClaimTypes.Role, ApplicationRoleNames.ADMIN),
                new Claim("roles", ApplicationRoleNames.ADMIN),
                new Claim(SystemUptimeTrackerClaimTypes.IS_ACTIVE, bool.FalseString)
            ], "External");

            ClaimsPrincipal jwtPrincipal = new([bearerIdentity, staleIdentity]);

            ClaimsPrincipal transformedPrincipal = await claimsTransformation.TransformAsync(jwtPrincipal);

            Assert.Multiple(() =>
            {
                Assert.That(staleIdentity.FindAll(ClaimTypes.Role).Select(claim => claim.Value), Is.EquivalentTo(new[] { ApplicationRoleNames.ADMIN }));
                Assert.That(staleIdentity.FindAll("roles").Select(claim => claim.Value), Is.EquivalentTo(new[] { ApplicationRoleNames.ADMIN }));
                Assert.That(staleIdentity.FindFirst(SystemUptimeTrackerClaimTypes.IS_ACTIVE)?.Value, Is.EqualTo(bool.FalseString));
                Assert.That(transformedPrincipal.FindAll(ClaimTypes.Role).Select(claim => claim.Value), Is.EquivalentTo(new[] { ApplicationRoleNames.CONTRIBUTOR }));
                Assert.That(transformedPrincipal.FindAll("roles").Select(claim => claim.Value), Is.EquivalentTo(new[] { ApplicationRoleNames.CONTRIBUTOR }));
                Assert.That(transformedPrincipal.FindFirstValue(SystemUptimeTrackerClaimTypes.IS_ACTIVE), Is.EqualTo(bool.TrueString));
            });
        }
    }

    [Test]
    public async Task IdentityApi_LocalCookieLogin_AllowsProtectedEndpoint_AndLogoutClearsSession()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient client = CreateCookieTrackingClient(app);
        string email = "member@example.test";
        const string PASSWORD = "Password1!";

        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email
            };

            IdentityResult createResult = await userManager.CreateAsync(user, PASSWORD);
            Assert.That(createResult.Succeeded, Is.True, string.Join("; ", createResult.Errors.Select(error => error.Description)));

            string confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
            IdentityResult confirmResult = await userManager.ConfirmEmailAsync(user, confirmationToken);
            Assert.That(confirmResult.Succeeded, Is.True, string.Join("; ", confirmResult.Errors.Select(error => error.Description)));
        }

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = email,
            Password = PASSWORD
        });

        Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        HttpResponseMessage protectedResponse = await client.GetAsync("/api/identity/manage/info");
        Assert.That(protectedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        HttpResponseMessage logoutResponse = await client.PostAsync("/api/identity/logout", null);

        Assert.That(logoutResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(logoutResponse.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? logoutCookies), Is.True);
        Assert.That(logoutCookies!.Any(cookie => cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase)), Is.True);

        HttpResponseMessage protectedResponseAfterLogout = await client.GetAsync("/api/identity/manage/info");
        Assert.That(protectedResponseAfterLogout.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task IdentityApi_WhenLoginSucceeds_PersistsLastLoginTimestamp()
    {
        string databaseName = $"SystemUptimeTrackerIdentityApi_{Guid.NewGuid():N}";

        await using WebApplication app = await CreateIdentityApiApplicationAsync(databaseName);
        HttpClient client = CreateCookieTrackingClient(app);
        const string EMAIL = "member@example.test";
        const string PASSWORD = "Password1!";

        using (IServiceScope scope = app.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = EMAIL,
                Email = EMAIL
            };

            IdentityResult createResult = await userManager.CreateAsync(user, PASSWORD);
            Assert.That(createResult.Succeeded, Is.True, string.Join("; ", createResult.Errors.Select(error => error.Description)));

            string confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
            IdentityResult confirmResult = await userManager.ConfirmEmailAsync(user, confirmationToken);
            Assert.That(confirmResult.Succeeded, Is.True, string.Join("; ", confirmResult.Errors.Select(error => error.Description)));
            Assert.That(user.LastLoginAtUtc, Is.Null);
        }

        DateTimeOffset loginStartedAtUtc = DateTimeOffset.UtcNow;

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/api/identity/login?useCookies=true", new
        {
            Email = EMAIL,
            Password = PASSWORD
        });

        Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using IServiceScope verificationScope = app.Services.CreateScope();
        UserManager<ApplicationUser> verificationUserManager = verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser refreshedUser = await verificationUserManager.FindByEmailAsync(EMAIL)
            ?? throw new AssertionException("Expected refreshed logged-in user.");

        Assert.Multiple(() =>
        {
            Assert.That(refreshedUser.LastLoginAtUtc, Is.Not.Null);
            Assert.That(refreshedUser.LastLoginAtUtc, Is.GreaterThanOrEqualTo(loginStartedAtUtc));
            Assert.That(refreshedUser.LastLoginAtUtc, Is.LessThanOrEqualTo(DateTimeOffset.UtcNow));
        });
    }

    private static async Task<WebApplication> CreateIdentityApiApplicationAsync(
        string databaseName,
        string? environmentName = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName ?? Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection();
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = SystemUptimeTrackerAuthenticationSchemes.ANTIFORGERY_HEADER_NAME;
        });
        builder.Services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.Zero;
        });
        builder.Services.AddAuthorization(SystemUptimeTrackerAuthorizationPolicyCatalog.Configure);
        AuthenticationBuilder authenticationBuilder = builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SystemUptimeTrackerAuthenticationSchemes.APPLICATION;
                options.DefaultChallengeScheme = SystemUptimeTrackerAuthenticationSchemes.APPLICATION;
                options.DefaultScheme = SystemUptimeTrackerAuthenticationSchemes.APPLICATION;
            })
            .AddPolicyScheme(SystemUptimeTrackerAuthenticationSchemes.APPLICATION, "SystemUptimeTracker test authentication", options =>
            {
                options.ForwardDefaultSelector = context => SystemUptimeTrackerAuthenticationSchemeSelector.Resolve(context, jwtEnabled: true);
            });
        authenticationBuilder.AddIdentityCookies();
        authenticationBuilder.AddScheme<AuthenticationSchemeOptions, TestBearerAuthenticationHandler>(
            IdentityConstants.BearerScheme,
            _ => { });
        authenticationBuilder.AddScheme<AuthenticationSchemeOptions, TestBearerAuthenticationHandler>(
            JwtBearerDefaults.AuthenticationScheme,
            _ => { });
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
        builder.Services.AddTransient<IClaimsTransformation, IdentityStoreClaimsTransformation>();
        builder.Services.AddSingleton<IEmailSender<ApplicationUser>, NoOpEmailSender>();
        builder.RegisterIdentityData((_, options) => options.UseInMemoryDatabase(databaseName));

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapSystemUptimeTrackerAntiforgeryEndpoints();

        RouteGroupBuilder identityGroup = app.MapGroup("/api/identity");
        identityGroup.MapSystemUptimeTrackerBootstrapIdentityEndpoints();

        identityGroup.MapIdentityApi<ApplicationUser>();
        identityGroup.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
            {
                await signInManager.SignOutAsync();
                return Results.NoContent();
            })
            .RequireAuthorization();
        app.MapSystemUptimeTrackerProtectedApplicationEndpoints();

        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateCookieTrackingClient(WebApplication app)
    {
        HttpClient client = new(new CookieTrackingHandler(app.GetTestServer().CreateHandler()));
        client.BaseAddress = new Uri("http://localhost");

        return client;
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
            if (string.Equals(Scheme.Name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal)
                && string.Equals(token, LOCAL_ADMIN_JWT_TOKEN, StringComparison.Ordinal))
            {
                return CreatePrincipal(JwtBearerDefaults.AuthenticationScheme,
                [
                    new Claim("sub", LOCAL_ADMIN_JWT_USER_ID)
                ]);
            }

            return null;
        }

        private static ClaimsPrincipal CreatePrincipal(string authenticationType, IEnumerable<Claim> claims)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
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

[NonParallelizable]
[TestFixture(Category = "Integration")]
public class ApplicationDbContextSqlServerTests
{
    private const string EXPECTED_MIGRATION_NAME = "20260725213503_InitialCreate";
    private const string SQL_SERVER_PROVIDER_NAME = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string DEFAULT_TEST_SERVER_CONNECTION_STRING = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True";
    private const string TEST_SERVER_CONNECTION_ENVIRONMENT_VARIABLE = "SystemUptimeTracker__Tests__SqlServer__ConnectionString";

    [Test]
    public async Task RegisterIdentityData_SqlServerProvider_CreatesMigratedDatabaseAndUsesIdentityStores()
    {
        await using SqlServerIdentityTestHost testHost = await CreateMigratedSqlServerIdentityTestHostAsync();

        ApplicationDbContext context = testHost.Scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        UserManager<ApplicationUser> userManager = testHost.Scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IUserStore<ApplicationUser> userStore = testHost.Scope.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = "user@example.test",
            Email = "user@example.test"
        };

        IdentityResult createResult = await userManager.CreateAsync(user, "Password1!");

        Assert.That(createResult.Succeeded, Is.True, string.Join("; ", createResult.Errors.Select(error => error.Description)));
        Assert.That(context.Database.ProviderName, Is.EqualTo(SQL_SERVER_PROVIDER_NAME));
        Assert.That(await context.Database.GetAppliedMigrationsAsync(), Does.Contain(EXPECTED_MIGRATION_NAME));
        Assert.That(await context.Users.CountAsync(), Is.EqualTo(1));
        Assert.That(await context.Roles.CountAsync(), Is.EqualTo(ApplicationRoleNames.All.Length));
        Assert.That(await userManager.FindByEmailAsync(user.Email), Is.Not.Null);
        foreach (string roleName in ApplicationRoleNames.All)
        {
            Assert.That(await userManager.IsInRoleAsync(user, roleName), Is.True, $"Expected the first SQL-backed user to receive role {roleName}.");
        }

        Assert.That(userStore, Is.Not.Null);
    }

    [Test]
    public async Task IdentityRoleSeeder_WhenSqlServerIdentityTablesExistWithoutMigrationHistory_SeedsRolesWithoutReapplyingInitialSchema()
    {
        await using SqlServerIdentityTestHost testHost = await CreateMigratedSqlServerIdentityTestHostAsync();

        ApplicationDbContext context = testHost.Scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IIdentityRoleSeeder roleSeeder = testHost.Scope.ServiceProvider.GetRequiredService<IIdentityRoleSeeder>();
        RoleManager<IdentityRole> roleManager = testHost.Scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM [__EFMigrationsHistory]");

        Assert.DoesNotThrowAsync(async () => await roleSeeder.EnsureSeedDataAsync());

        foreach (string roleName in ApplicationRoleNames.All)
        {
            Assert.That(await roleManager.RoleExistsAsync(roleName), Is.True, $"Expected role '{roleName}' to be seeded against an existing identity schema.");
        }
    }

    private static async Task<SqlServerIdentityTestHost> CreateMigratedSqlServerIdentityTestHostAsync()
    {
        string databaseName = $"SystemUptimeTrackerIdentityTests_{Guid.NewGuid():N}";
        string baseConnectionString = ResolveBaseSqlServerConnectionString();
        string databaseConnectionString = BuildDatabaseConnectionString(baseConnectionString, databaseName);

        await CreateDatabaseAsync(baseConnectionString, databaseName);

        WebApplication? app = null;
        IServiceScope? scope = null;

        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.Services.AddDataProtection();
            builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
                .AddIdentityCookies();

            builder.RegisterIdentityData((_, options) => options.UseSqlServer(databaseConnectionString));

            app = builder.Build();
            scope = app.Services.CreateScope();
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await context.Database.MigrateAsync();

            return new SqlServerIdentityTestHost(baseConnectionString, databaseName, app, scope);
        }
        catch
        {
            scope?.Dispose();

            if (app is not null)
            {
                await app.DisposeAsync();
            }

            await DropDatabaseAsync(baseConnectionString, databaseName);
            throw;
        }
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
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string baseConnectionString, string databaseName)
    {
        await using var connection = new SqlConnection(baseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{databaseName}') IS NOT NULL " +
            $"BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class SqlServerIdentityTestHost : IAsyncDisposable
    {
        public SqlServerIdentityTestHost(string baseConnectionString, string databaseName, WebApplication app, IServiceScope scope)
        {
            BaseConnectionString = baseConnectionString;
            DatabaseName = databaseName;
            App = app;
            Scope = scope;
        }

        public string BaseConnectionString { get; }

        public string DatabaseName { get; }

        public WebApplication App { get; }

        public IServiceScope Scope { get; }

        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await App.DisposeAsync();
            await DropDatabaseAsync(BaseConnectionString, DatabaseName);
        }
    }
}
