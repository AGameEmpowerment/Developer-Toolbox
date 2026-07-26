using SystemUptimeTracker.Common.Connection;
using SystemUptimeTracker.Common.Connection.Interfaces;

namespace SystemUptimeTracker.Api.Connection.DependencyInjection;

public static class ConnectionResolver
{
    public static void RegisterDependencies(IServiceCollection service)
    {
        service.AddScoped<IHttpClientWrapper, HttpClientWrapper>();
    }
}