using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;

namespace DeveloperToolbox.Example.Messaging;

// Share the same local-only connection contract between sends and live validators.
internal static class LocalServiceBusConnection
{
    internal static (string ConnectionString, Uri Endpoint) Read(IConfiguration configuration)
    {
        var connectionString = configuration["ServiceBusConnection"]
            ?? throw new InvalidOperationException("Set ServiceBusConnection for the local emulator.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator < 1)
            {
                throw new InvalidOperationException("Ambiguous or unsupported local Service Bus connection setting.");
            }
            var key = part[..separator];
            if (key is not ("Endpoint" or "SharedAccessKeyName" or "SharedAccessKey" or "UseDevelopmentEmulator")
                || !values.TryAdd(key, part[(separator + 1)..]))
            {
                throw new InvalidOperationException("Ambiguous or unsupported local Service Bus connection setting.");
            }
        }
        if (!values.TryGetValue("UseDevelopmentEmulator", out var emulator)
            || !string.Equals(emulator, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This sample requires a Service Bus development emulator connection.");
        }
        if (!values.TryGetValue("Endpoint", out var endpointValue)
            || !Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != "sb" || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
            || endpoint.AbsolutePath is not ("" or "/")
            || (!endpoint.IsLoopback && !string.Equals(endpoint.Host, "service-bus", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The Service Bus example requires the local toolbox emulator endpoint.");
        }
        // Verify the installed SDK selects the same destination as the strict guard.
        var properties = ServiceBusConnectionStringProperties.Parse(connectionString);
        if (endpoint != properties.Endpoint)
        {
            throw new InvalidOperationException("The Service Bus SDK selected a different endpoint from the local guard.");
        }
        return (connectionString, endpoint);
    }
}
