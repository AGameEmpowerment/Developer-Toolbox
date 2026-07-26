using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SystemUptimeTracker.Api.Services.Identity;
using SystemUptimeTracker.Data.Identity;
using SystemUptimeTracker.Qa.Automation.Configuration;
using SystemUptimeTracker.Qa.Automation.Pages;
using SystemUptimeTracker.Qa.Automation.Services;
using SystemUptimeTracker.Qa.Automation.Support;

namespace SystemUptimeTracker.Qa.Automation;

public static class RegisterDependentServices
{
    private const string SHARED_QA_DATABASE_CONNECTION_STRING_NAME = "SharedQaDatabase";
    private const string SHARED_QA_DATABASE_PLACEHOLDER = "__SET_IN_USER_SECRETS_OR_ENV__";
    private const string MAIN_APPLICATION_DATABASE_NAME = "SystemUptimeTracker";
    private const string DEFAULT_LOCAL_QA_DATABASE_CONNECTION_STRING = "Server=127.0.0.1,10433;Database=SystemUptimeTracker_QaAutomation;User Id=sa;Password=P@ssword123!;Encrypt=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public static IServiceCollection RegisterQaAutomationServices(
        this IServiceCollection services,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);

        IConfiguration configuration = RegisterConfiguration(environmentName);

        services.AddSingleton(configuration);
        services.AddSingleton<IHostEnvironment>(_ => new QaAutomationHostEnvironment(environmentName));
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddConsole();
        });

        RegisterOptions(services);
        RegisterFrameworkServices(services);
        RegisterIdentityAutomationServices(services, configuration);
        RegisterApplicationServices(services);
        RegisterPageObjects(services);

        return services;
    }

    private static IConfiguration RegisterConfiguration(string environmentName)
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
            .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
            .AddUserSecrets(typeof(SystemUptimeTracker.Api.RegisterDependentServices).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static void RegisterOptions(IServiceCollection services)
    {
        services.AddOptions<AutomationAppSettings>()
            .Configure<IConfiguration>((options, configuration) =>
                configuration.GetSection("AppSettings").Bind(options))
            .ValidateOnStart();

        services.AddOptions<QaAutomationExecutionOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                configuration.GetSection(QaAutomationExecutionOptions.SECTION_NAME).Bind(options))
            .Validate(
                options => !options.UseExternalHost || !string.IsNullOrWhiteSpace(options.WebBaseUrl),
                "QaAutomation:WebBaseUrl is required when QaAutomation:UseExternalHost is true.")
            .ValidateOnStart();

        services.AddOptions<SystemUptimeTrackerWebValidationOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                configuration.GetSection(SystemUptimeTrackerWebValidationOptions.SECTION_NAME).Bind(options))
            .ValidateDataAnnotations()
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.BaseUrl),
                "TestConfiguration:WebValidation:BaseUrl is required.")
            .ValidateOnStart();
    }

    private static void RegisterFrameworkServices(IServiceCollection services)
    {
        services.AddSingleton<IApiClientFactory, ApiClientFactory>();
        services.AddSingleton<IPlaywrightBrowserEnvironment, PlaywrightBrowserEnvironment>();
        services.AddSingleton<IPlaywrightBrowserFactory, PlaywrightBrowserFactory>();
        services.AddSingleton<IPlaywrightPageSessionFactory, PlaywrightPageSessionFactory>();
        services.AddSingleton<IPageObjectFactory, PageObjectFactory>();
    }

    private static void RegisterIdentityAutomationServices(IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = ResolveRuntimeAutomationDatabaseConnectionString(configuration);

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });

        services.AddHttpContextAccessor();
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services
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
            .AddDefaultTokenProviders();

        services.AddScoped<IIdentityRoleSeeder, IdentityRoleSeeder>();
        services.AddScoped<UserManager<ApplicationUser>, ApplicationUserManager>();
        services.AddScoped<SignInManager<ApplicationUser>, ApplicationSignInManager>();
    }

    private static string ResolveRuntimeAutomationDatabaseConnectionString(IConfiguration configuration)
    {
        string? applicationConnectionString = configuration.GetConnectionString("DefaultConnection");
        return !string.IsNullOrWhiteSpace(applicationConnectionString)
            ? applicationConnectionString
            : ResolveQaDatabaseConnectionString(configuration);
    }

    private static void RegisterApplicationServices(IServiceCollection services)
    {
        services.AddSingleton<ISystemUptimeTrackerPageCatalog, SystemUptimeTrackerPageCatalog>();
        services.AddSingleton<ISystemUptimeTrackerApiClient, SystemUptimeTrackerApiClient>();
        services.AddScoped<ITestDatabaseCleanupService, TestDatabaseCleanupService>();
        services.AddScoped<ITestIdentityAccountCleanupService, TestIdentityAccountCleanupService>();
        services.AddScoped<ITestIdentityAccountProvisioningService, TestIdentityAccountProvisioningService>();
    }

    internal static string ResolveQaDatabaseConnectionString(string environmentName)
    {
        IConfiguration configuration = RegisterConfiguration(environmentName);
        return ResolveQaDatabaseConnectionString(configuration);
    }

    internal static string ResolveQaDatabaseConnectionString(IConfiguration configuration)
    {
        string configuredConnectionString = configuration.GetConnectionString(SHARED_QA_DATABASE_CONNECTION_STRING_NAME) ?? string.Empty;
        string resolvedConnectionString = string.IsNullOrWhiteSpace(configuredConnectionString) ||
               string.Equals(configuredConnectionString, SHARED_QA_DATABASE_PLACEHOLDER, StringComparison.OrdinalIgnoreCase)
            ? DEFAULT_LOCAL_QA_DATABASE_CONNECTION_STRING
            : configuredConnectionString;

        ValidateQaDatabaseIsolation(configuration, resolvedConnectionString);

        return resolvedConnectionString;
    }

    private static void ValidateQaDatabaseIsolation(
        IConfiguration configuration,
        string connectionString)
    {
        bool allowMainDatabase = configuration.GetValue<bool>(
            $"{QaAutomationExecutionOptions.SECTION_NAME}:{nameof(QaAutomationExecutionOptions.AllowMainDatabase)}");

        if (allowMainDatabase)
        {
            return;
        }

        SqlConnectionStringBuilder connectionStringBuilder = new(connectionString);

        if (!string.Equals(
                connectionStringBuilder.InitialCatalog,
                MAIN_APPLICATION_DATABASE_NAME,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException(
            "QA automation must not run against the main SystemUptimeTracker database. " +
            "Set ConnectionStrings:SharedQaDatabase to a dedicated QA database such as SystemUptimeTracker_QaAutomation. " +
            "Only set QaAutomation:AllowMainDatabase=true for deliberate local cleanup or debugging.");
    }

    private static void RegisterPageObjects(IServiceCollection services)
    {
        services.AddTransient<SystemUptimeTrackerHomePage>();
        services.AddTransient<SystemUptimeTrackerAdminUsersPage>();
        services.AddTransient<LoginPage>();
    }

    private sealed class QaAutomationHostEnvironment : IHostEnvironment
    {
        public QaAutomationHostEnvironment(string environmentName)
        {
            EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? Environments.Development : environmentName;
            ApplicationName = typeof(RegisterDependentServices).Assembly.GetName().Name ?? "SystemUptimeTracker.Qa.Automation";
            ContentRootPath = AppContext.BaseDirectory;
            ContentRootFileProvider = new PhysicalFileProvider(ContentRootPath);
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; }

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
