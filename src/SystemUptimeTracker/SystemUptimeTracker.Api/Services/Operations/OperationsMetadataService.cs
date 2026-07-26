using SystemUptimeTracker.Api.Models.Operations;
using SystemUptimeTracker.Api.Services.Operations.Interface;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace SystemUptimeTracker.Api.Services.Operations;

public sealed class OperationsMetadataService : IOperationsMetadataService
{
    private readonly ILogger<OperationsMetadataService> _logger;
    private readonly OperationsMetadataResponse _metadata;

    public OperationsMetadataService(IHostEnvironment hostEnvironment, DateTimeOffset startedAtUtc, ILogger<OperationsMetadataService> logger)
    {
        ArgumentNullException.ThrowIfNull(hostEnvironment);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Assembly assembly = typeof(OperationsMetadataService).Assembly;
        Version? assemblyVersion = assembly.GetName().Version;
        string applicationVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?
            .Split('+', 2)[0]
            ?? assemblyVersion?.ToString()
            ?? "0.0.0";

        _metadata = new OperationsMetadataResponse
        {
            ApplicationName = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
                ?? hostEnvironment.ApplicationName
                ?? "SystemUptimeTracker.Api",
            ApplicationVersion = applicationVersion,
            BuildVersion = assemblyVersion?.ToString() ?? applicationVersion,
            Environment = hostEnvironment.EnvironmentName,
            StartedAtUtc = startedAtUtc
        };

        _logger.LogInformation("Operations metadata initialized. Environment={Environment}; StartedAtUtc={StartedAtUtc}; ApplicationVersion={ApplicationVersion}.", _metadata.Environment, _metadata.StartedAtUtc, _metadata.ApplicationVersion);
    }

    public OperationsMetadataResponse GetMetadata()
    {
        _logger.LogDebug("Operations metadata requested.");
        return _metadata;
    }
}