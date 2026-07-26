using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SystemUptimeTracker.Api.Models.ApplicationSettings;
using SystemUptimeTracker.Api.Services.Identity;
using SystemUptimeTracker.Common.Helpers.Data;
using DataRepositoriesResolver = SystemUptimeTracker.Data.DependencyInjection.RepositoriesResolver;
using SystemUptimeTracker.Data.Identity;

namespace SystemUptimeTracker.Api.Repositories.DependencyInjection;

public static class RepositoriesResolver
{
    public static void RegisterDependencies(IServiceCollection service, AppSettings appSettings)
    {
        if (string.IsNullOrWhiteSpace(appSettings.ConnectionStrings.DefaultConnection))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
        }

        // Register application DB contexts and repositories here as the domain grows.
        DataRepositoriesResolver.RegisterDependencies(service);
    }

    public static void RegisterIdentityData(
        this WebApplicationBuilder builder,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            if (configureDbContext is not null)
            {
                configureDbContext(serviceProvider, options);
                return;
            }

            var appSettings = serviceProvider.GetRequiredService<IOptions<AppSettings>>().Value;
            if (string.IsNullOrWhiteSpace(appSettings.ConnectionStrings.DefaultConnection))
            {
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
            }

            ConfigureSqlServerOptions(
                options,
                appSettings.ConnectionStrings.DefaultConnection,
                appSettings.FeatureManagement.SqlDebugger);
        });

        builder.Services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.User.RequireUniqueEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager<ApplicationSignInManager>()
            .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>()
            .AddApiEndpoints()
            .AddDefaultTokenProviders();

        builder.Services.AddScoped<SignInManager<ApplicationUser>, ApplicationSignInManager>();

        builder.Services.TryAddSingleton<IEmailSender<ApplicationUser>, LoggingEmailSender>();
        builder.Services.AddScoped<IIdentityRoleSeeder, IdentityRoleSeeder>();
        builder.Services.AddScoped<UserManager<ApplicationUser>, ApplicationUserManager>();
    }

    private static void ConfigureSqlServerOptions(
        DbContextOptionsBuilder options,
        string connectionString,
        bool enableSqlDebugger)
    {
        options.UseSqlServer(connectionString);

        if (!enableSqlDebugger)
        {
            return;
        }

        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
        options.UseLoggerFactory(LoggerSupport.GetLoggerFactory());
    }
}