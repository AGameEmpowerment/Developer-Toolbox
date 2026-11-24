using DeveloperToolbox.Example.Data;
using DeveloperToolbox.Example.Messaging;
using DeveloperToolbox.Example.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperToolbox.Example.Functions.Tests;

public sealed class FunctionRegistrationTests
{
    [Test]
    public async Task AddToolboxFunctionSamples_ResourceHttpHandler_DisablesRedirectsAndProxies()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddLogging();
        services.AddToolboxFunctionSamples(configuration);
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        var handler = factory.CreateHandler(ResourceSamples.HttpClientName);
        while (handler is DelegatingHandler wrapper)
        {
            handler = wrapper.InnerHandler!;
        }

        Assert.That(handler, Is.TypeOf<HttpClientHandler>());
        var primary = (HttpClientHandler)handler;
        Assert.That(primary.AllowAutoRedirect, Is.False);
        Assert.That(primary.UseProxy, Is.False);
    }

    [Test]
    public async Task AddToolboxFunctionSamples_WithoutCredentials_ResolvesBothFunctionsWithoutNetworkCalls()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddLogging();
        services.AddToolboxFunctionSamples(configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.That(provider.GetRequiredService<ResourceFunctions>(), Is.Not.Null);
        Assert.That(provider.GetRequiredService<QueueFunctions>(), Is.Not.Null);
        Assert.That(provider.GetRequiredService<IResourceSamples>(), Is.TypeOf<ResourceSamples>());
        Assert.That(provider.GetRequiredService<IMessagingSamples>(), Is.TypeOf<MessagingSamples>());
        Assert.That(provider.GetRequiredService<IDatabaseSamples>(), Is.TypeOf<DatabaseSamples>());
        Assert.That(provider.GetRequiredService<IQueuePublishers>(), Is.TypeOf<QueuePublishers>());
    }
}
