using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperToolbox.Example.Data;

/// <summary>Shared registration for CLI, Functions, and Lambda samples.</summary>
public static class DatabaseRegistration
{
    /// <summary>Registers the local database facade without opening either database at startup.</summary>
    public static IServiceCollection AddToolboxDatabases(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ => new DatabaseSamples(configuration));
        services.AddSingleton<IDatabaseSamples>(provider => provider.GetRequiredService<DatabaseSamples>());
        return services;
    }
}
