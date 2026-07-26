using SystemUptimeTracker.Api.Services.Info;
using SystemUptimeTracker.Api.Services.Info.Interface;
using SystemUptimeTracker.Api.Services.Operations;
using SystemUptimeTracker.Api.Services.Operations.Interface;

namespace SystemUptimeTracker.Api.Services.DependencyInjection;

public static class ServicesResolver
{
    public static void RegisterDependencies(IServiceCollection service)
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

        service.AddScoped<IInfoService, InfoService>();
        service.AddSingleton<IOperationsMetadataService>(sp =>
            new OperationsMetadataService(
                sp.GetRequiredService<IHostEnvironment>(),
                startedAtUtc,
                sp.GetRequiredService<ILogger<OperationsMetadataService>>()));
    }
}
