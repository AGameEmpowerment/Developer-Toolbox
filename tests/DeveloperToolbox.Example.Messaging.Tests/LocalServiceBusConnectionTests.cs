using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace DeveloperToolbox.Example.Messaging.Tests;

public sealed class LocalServiceBusConnectionTests
{
    [TestCase("localhost:10170")]
    [TestCase("127.0.0.2:10170")]
    [TestCase("[::1]:10170")]
    [TestCase("service-bus:5672")]
    public async Task Read_CanonicalLocalConnection_MatchesSdkSelectedEndpoint(string host)
    {
        var connection = $"Endpoint=sb://{host}/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture==;UseDevelopmentEmulator=true;";

        var result = LocalServiceBusConnection.Read(Configuration(connection));

        Assert.That(result.ConnectionString, Is.EqualTo(connection));
        await using var client = new ServiceBusClient(result.ConnectionString);
        Assert.That(result.Endpoint, Is.EqualTo(new Uri($"sb://{host}/")));
        Assert.That(client.FullyQualifiedNamespace, Is.EqualTo(result.Endpoint.Host));
    }

    [TestCase("UseDevelopmentEmulator = true")]
    [TestCase(" UseDevelopmentEmulator=true")]
    [TestCase("usedevelopmentemulator=true")]
    [TestCase("UseDevelopmentEmulator=true;UseDevelopmentEmulator=false")]
    [TestCase("UseDevelopmentEmulator=true;usedevelopmentemulator=false")]
    [TestCase("UseDevelopmentEmulator=true;Endpoint=sb://remote.example/")]
    [TestCase("UseDevelopmentEmulator=true;Endpoint=sb://localhost/")]
    [TestCase("UseDevelopmentEmulator=true;endpoint=sb://remote.example/")]
    [TestCase("UseDevelopmentEmulator=true; Endpoint=sb://remote.example/")]
    [TestCase("UseDevelopmentEmulator=true;Endpoint =sb://remote.example/")]
    [TestCase("UseDevelopmentEmulator=true;SharedAccessKey=another")]
    [TestCase("UseDevelopmentEmulator=true;SharedAccessKeyName=another")]
    [TestCase("UseDevelopmentEmulator=true;EntityPath=queue")]
    [TestCase("UseDevelopmentEmulator=true;malformed")]
    public void Read_NoncanonicalOrDuplicateFields_RejectsAmbiguousConnection(string fields)
    {
        var connection = $"Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture;{fields};";

        Assert.Throws<InvalidOperationException>(() => LocalServiceBusConnection.Read(Configuration(connection)));
    }

    [TestCase("sb://remote.example/")]
    [TestCase("https://localhost/")]
    [TestCase("sb://user:secret@localhost/")]
    [TestCase("sb://localhost/queue")]
    [TestCase("sb://localhost/?redirect=cloud")]
    [TestCase("sb://localhost/#fragment")]
    public void Read_UnsafeEndpoint_RejectsConnection(string endpoint)
    {
        var connection = $"Endpoint={endpoint};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture;UseDevelopmentEmulator=true;";

        Assert.Throws<InvalidOperationException>(() => LocalServiceBusConnection.Read(Configuration(connection)));
    }

    [Test]
    public void Read_EmulatorTextInsideKeyName_RejectsMissingFlag()
    {
        var connection = "Endpoint=sb://localhost/;SharedAccessKeyName=UseDevelopmentEmulator=true;SharedAccessKey=fixture;";

        Assert.Throws<InvalidOperationException>(() => LocalServiceBusConnection.Read(Configuration(connection)));
    }

    [Test]
    public void Read_EmulatorFlagIsFalse_RejectsLocalEndpointWithoutEmulatorMode()
    {
        var connection = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fixture;UseDevelopmentEmulator=false;";

        Assert.Throws<InvalidOperationException>(() => LocalServiceBusConnection.Read(Configuration(connection)));
    }

    private static IConfiguration Configuration(string connection) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceBusConnection"] = connection }).Build();
}
