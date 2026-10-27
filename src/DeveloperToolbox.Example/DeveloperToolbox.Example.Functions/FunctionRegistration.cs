using DeveloperToolbox.Example.Data;
using DeveloperToolbox.Example.Messaging;
using DeveloperToolbox.Example.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperToolbox.Example.Functions;

/// <summary>Registers the resource and queue operations used by the local Functions host.</summary>
public static class FunctionRegistration
{
    /// <summary>Registers sample services without contacting external resources at startup.</summary>
    public static IServiceCollection AddToolboxFunctionSamples(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(ResourceSamples.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        services.AddToolboxDatabases(configuration);
        services.AddSingleton<IResourceSamples, ResourceSamples>();
        services.AddSingleton<IMessagingSamples, MessagingSamples>();
        services.AddSingleton<IQueuePublishers, QueuePublishers>();
        services.AddTransient<ResourceFunctions>();
        services.AddTransient<QueueFunctions>();
        return services;
    }
}
