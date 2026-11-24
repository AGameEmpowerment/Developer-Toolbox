using DeveloperToolbox.Example.App;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NUnit.Framework;

namespace DeveloperToolbox.Example.App.Tests;

public sealed class ToolboxEnvironmentTests
{
    [Test]
    public void Parse_CanonicalReferences_ProtectConnectionComponentsAndKeepEnvironmentSecretsLiteral()
    {
        var password = "a;Server=external.example;\"$UNCHANGED";
        var values = ToolboxEnvironment.Parse("""
            MSSQL_SA_PASSWORD=first
            MSSQL_SA_PASSWORD=second
            POSTGRES_USER='local;role'
            POSTGRES_PASSWORD='pg;"$LITERAL'
            ConnectionStrings__SqlServer="Server=127.0.0.1,10433;Database=ToolboxSamples;Password=${MSSQL_SA_PASSWORD};Encrypt=True"
            ConnectionStrings__Postgres="Host=localhost;Database=ToolboxSamples;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
            Resources__S3__Endpoint="${TOOLBOX_AWS_ENDPOINT:-http://localhost:10195}"
            Resources__Seq__ApiKey="prefix\$LITERAL$$suffix"
            UNKNOWN_SECRET=ignored
            LOCALSTACK_AUTH_TOKEN=ignored
            """, new Dictionary<string, string> { ["MSSQL_SA_PASSWORD"] = password });

        var sql = new SqlConnectionStringBuilder(values["ConnectionStrings:SqlServer"]);
        Assert.That(sql.Password, Is.EqualTo(password));
        Assert.That(sql.DataSource, Is.EqualTo("127.0.0.1,10433"));
        var postgres = new NpgsqlConnectionStringBuilder(values["ConnectionStrings:Postgres"]);
        Assert.That(postgres.Username, Is.EqualTo("local;role"));
        Assert.That(postgres.Password, Is.EqualTo("pg;\"$LITERAL"));
        Assert.That(postgres.Host, Is.EqualTo("localhost"));
        Assert.That(values["Resources:S3:Endpoint"], Is.EqualTo("http://localhost:10195"));
        Assert.That(values["Resources:Seq:ApiKey"], Is.EqualTo("prefix$LITERAL$suffix"));
        Assert.That(values.Keys, Does.Not.Contain("UNKNOWN_SECRET"));
        Assert.That(values.Keys, Does.Not.Contain("LOCALSTACK_AUTH_TOKEN"));
    }

    [Test]
    public void Parse_QuotedAndUnquotedAssignments_PreserveDataWithoutExecutingCommands()
    {
        var values = ToolboxEnvironment.Parse("""
            export RABBITMQ_DEFAULT_PASS='$(external-command) $UNEXPANDED'
            Resources__Seq__ApiKey="first" # replaced below
            Resources__Seq__ApiKey="last # literal"
            Resources__WireMock__Endpoint=http://localhost:10080 # comment
            Resources__S3__Bucket=bucket#literal
            """, new Dictionary<string, string>());

        Assert.That(values["RABBITMQ_DEFAULT_PASS"], Is.EqualTo("$(external-command) $UNEXPANDED"));
        Assert.That(values["Resources:Seq:ApiKey"], Is.EqualTo("last # literal"));
        Assert.That(values["Resources:WireMock:Endpoint"], Is.EqualTo("http://localhost:10080"));
        Assert.That(values["Resources:S3:Bucket"], Is.EqualTo("bucket#literal"));
    }

    [TestCase("Resources__Seq__ApiKey='sensitive-value")]
    [TestCase("Resources__Seq__ApiKey=\"sensitive-value\" trailing")]
    [TestCase("Resources__Seq__ApiKey=${Resources__Seq__ApiKey}")]
    [TestCase("Resources__Seq__ApiKey=${NAME:?sensitive-value}")]
    [TestCase("Resources__Seq__ApiKey=${NAME:-${OTHER}}")]
    [TestCase("Resources__Seq__ApiKey=${LOCALSTACK_AUTH_TOKEN}")]
    [TestCase("invalid-name=sensitive-value")]
    [TestCase("Resources__Seq__ApiKey=sensitive-value\0")]
    public void Parse_InvalidSettings_RejectWithoutIncludingSecretValues(string text)
    {
        var exception = Assert.Throws<FormatException>(() => ToolboxEnvironment.Parse(text, new Dictionary<string, string>()));
        Assert.That(exception!.Message, Does.Not.Contain("sensitive-value"));
    }

    [Test]
    public void Read_DotenvWithoutJson_AllowsIndependentHostConfigurationAndLaterOverrides()
    {
        var directory = Path.Combine(Path.GetTempPath(), "toolbox-dotnet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, ".env.example"), "Resources__Mail__Host=localhost\nResources__Mail__Port=10130\n");
            File.WriteAllText(Path.Combine(directory, ".env"), "Resources__Mail__Port=10131\n");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(ToolboxEnvironment.Read(Path.Combine(directory, ".env")))
                .AddJsonFile(Path.Combine(directory, "appsettings.json"), optional: true)
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Resources:Mail:Host"] = "127.0.0.1" })
                .Build();

            Assert.That(configuration["Resources:Mail:Host"], Is.EqualTo("127.0.0.1"));
            Assert.That(configuration["Resources:Mail:Port"], Is.EqualTo("10131"));
            Assert.That(File.Exists(Path.Combine(directory, "appsettings.json")), Is.False);
            File.WriteAllText(Path.Combine(directory, "appsettings.json"), """{"Resources":{"Mail":{"Port":"10132"}}}""");
            var withJson = new ConfigurationBuilder().AddInMemoryCollection(ToolboxEnvironment.Read(Path.Combine(directory, ".env")))
                .AddJsonFile(Path.Combine(directory, "appsettings.json"), optional: true).Build();
            Assert.That(withJson["Resources:Mail:Port"], Is.EqualTo("10132"));
        }
        finally
        {
            File.Delete(Path.Combine(directory, ".env.example"));
            File.Delete(Path.Combine(directory, ".env"));
            File.Delete(Path.Combine(directory, "appsettings.json"));
            Directory.Delete(directory);
        }
    }
}
