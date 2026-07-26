using SystemUptimeTracker.Api.Factories.Lookups;
using SystemUptimeTracker.Api.Factories.Lookups.Interfaces;
using SystemUptimeTracker.Api.Factories.Users;
using SystemUptimeTracker.Api.Factories.Users.Interfaces;

namespace SystemUptimeTracker.Api.Factories.DependencyInjection;

public static class FactoriesResolver
{
    public static void RegisterDependencies(IServiceCollection service)
    {
        service.AddScoped<IUiPermissionFactory, UiPermissionFactory>();
        service.AddScoped<ICountryFactory, CountryFactory>();
    }
}
