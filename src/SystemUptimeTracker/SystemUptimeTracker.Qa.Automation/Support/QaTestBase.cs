using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemUptimeTracker.Qa.Automation;
using SystemUptimeTracker.Qa.Automation.Services;

namespace SystemUptimeTracker.Qa.Automation.Support;

public abstract class QaTestBase
{
    protected IServiceProvider Services { get; private set; } = null!;

    protected ILogger Logger { get; private set; } = null!;

    protected AutomationAppSettings AppSettings { get; private set; } = null!;

    protected QaAutomationExecutionOptions QaAutomationExecution { get; private set; } = null!;

    protected virtual bool IncludeKeyVault => false;

    protected virtual string EnvironmentName => "Development";

    protected virtual string[] CreateHostArgs()
    {
        return [];
    }

    protected T GetRequiredService<T>()
        where T : notnull
    {
        return Services.GetRequiredService<T>();
    }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var services = new ServiceCollection();
        services.RegisterQaAutomationServices(EnvironmentName);

        Services = services.BuildServiceProvider(validateScopes: true);
        Logger = Services.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
        AppSettings = Services.GetRequiredService<IOptions<AutomationAppSettings>>().Value;
        QaAutomationExecution = Services.GetRequiredService<IOptions<QaAutomationExecutionOptions>>().Value;

        if (!QaAutomationExecution.SkipDatabaseCleanup)
        {
            await ResetQaDatabaseAsync("setup");
        }

        await OnOneTimeSetUp();
        if (!QaAutomationExecution.SkipIdentityCleanup)
        {
            await DeleteQaIdentityArtifactsAsync("setup");
        }
    }

    [SetUp]
    public async Task SetUp()
    {
        await OnSetUp();
    }

    [TearDown]
    public async Task TearDown()
    {
        await OnTearDown();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        try
        {
            if (!QaAutomationExecution.SkipIdentityCleanup)
            {
                await DeleteQaIdentityArtifactsAsync("teardown");
            }

            await OnOneTimeTearDown();
        }
        finally
        {
            if (Services is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (Services is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    protected virtual Task OnOneTimeSetUp()
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnSetUp()
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnTearDown()
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnOneTimeTearDown()
    {
        return Task.CompletedTask;
    }

    private async Task DeleteQaIdentityArtifactsAsync(string phase)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ITestIdentityAccountCleanupService? cleanupService = scope.ServiceProvider.GetService<ITestIdentityAccountCleanupService>();
        if (cleanupService is null)
        {
            return;
        }

        int deletedCount = await cleanupService.DeleteProvisionedAccountsAsync();

        if (deletedCount > 0)
        {
            Logger.LogInformation(
                "Deleted {UserCount} QA identity account artifact(s) during one-time {Phase}.",
                deletedCount,
                phase);
        }
    }

    private async Task ResetQaDatabaseAsync(string phase)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ITestDatabaseCleanupService? cleanupService = scope.ServiceProvider.GetService<ITestDatabaseCleanupService>();
        if (cleanupService is null)
        {
            Logger.LogWarning("ITestDatabaseCleanupService is not registered; skipping database reset during {Phase}.", phase);
            return;
        }

        try
        {
            await cleanupService.ResetDatabaseAsync();
            Logger.LogInformation("QA database reset completed successfully during one-time {Phase}.", phase);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to reset QA database during one-time {Phase}. Subsequent tests may fail if database state is invalid.", phase);
            throw;
        }
    }

}
