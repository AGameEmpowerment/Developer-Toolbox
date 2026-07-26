using SystemUptimeTracker.Api.Helpers.Interfaces;

namespace SystemUptimeTracker.Api.Helpers.DependencyInjection;

public static class HelpersResolver
{
    public static void RegisterDependencies(IServiceCollection service)
    {
        service.AddScoped<IControllerDependencyBundle, ControllerDependencyBundle>();
    }
}