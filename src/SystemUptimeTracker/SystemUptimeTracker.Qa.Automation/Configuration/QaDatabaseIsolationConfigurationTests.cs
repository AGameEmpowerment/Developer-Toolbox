using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SystemUptimeTracker.Qa.Automation.Support;

namespace SystemUptimeTracker.Qa.Automation.Configuration;

public sealed class QaDatabaseIsolationConfigurationTests
{
    private const string MAIN_DATABASE_CONNECTION_STRING = "Server=127.0.0.1,10433;Database=SystemUptimeTracker;User Id=sa;Password=P@ssword123!;Encrypt=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    [Test]
    public void ResolveQaDatabaseConnectionString_WhenSharedQaDatabaseIsMissing_UsesDedicatedQaDatabase()
    {
        IConfiguration configuration = CreateConfiguration();

        string connectionString = RegisterDependentServices.ResolveQaDatabaseConnectionString(configuration);

        SqlConnectionStringBuilder connectionStringBuilder = new(connectionString);
        Assert.That(connectionStringBuilder.InitialCatalog, Is.EqualTo("SystemUptimeTracker_QaAutomation"));
    }

    [Test]
    public void ResolveQaDatabaseConnectionString_WhenMainDatabaseConfigured_Throws()
    {
        IConfiguration configuration = CreateConfiguration(
            new KeyValuePair<string, string?>(
                "ConnectionStrings:SharedQaDatabase",
                MAIN_DATABASE_CONNECTION_STRING));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            RegisterDependentServices.ResolveQaDatabaseConnectionString(configuration))!;

        Assert.That(exception.Message, Does.Contain("main SystemUptimeTracker database"));
    }

    [Test]
    public void ResolveQaDatabaseConnectionString_WhenMainDatabaseExplicitlyAllowed_ReturnsConfiguredConnection()
    {
        IConfiguration configuration = CreateConfiguration(
            new KeyValuePair<string, string?>(
                "ConnectionStrings:SharedQaDatabase",
                MAIN_DATABASE_CONNECTION_STRING),
            new KeyValuePair<string, string?>(
                $"{QaAutomationExecutionOptions.SECTION_NAME}:AllowMainDatabase",
                "true"));

        string connectionString = RegisterDependentServices.ResolveQaDatabaseConnectionString(configuration);

        SqlConnectionStringBuilder connectionStringBuilder = new(connectionString);
        Assert.That(connectionStringBuilder.InitialCatalog, Is.EqualTo("SystemUptimeTracker"));
    }

    private static IConfiguration CreateConfiguration(
        params KeyValuePair<string, string?>[] configurationValues)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
    }
}
