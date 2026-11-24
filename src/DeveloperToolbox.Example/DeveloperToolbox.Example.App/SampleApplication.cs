using System.Text.Json;
using DeveloperToolbox.Example.Data;
using DeveloperToolbox.Example.Lib;
using DeveloperToolbox.Example.Messaging;
using DeveloperToolbox.Example.Resources;

namespace DeveloperToolbox.Example.App;

/// <summary>Dispatches CLI examples without coupling argument handling to host startup.</summary>
internal static class SampleApplication
{
    internal static readonly IReadOnlyList<string> ResourceNames = Array.AsReadOnly(new[]
    {
        "sqlserver", "postgres", "cosmos", "dynamodb", "redis", "mail", "seq", "wiremock",
        "blob", "storage-queue", "table", "rabbitmq", "sqs", "sns", "eventbridge", "s3",
        "servicebus", "servicebus-topic"
    });

    /// <summary>Returns the CLI exit code; clients initialize only for a supported example.</summary>
    internal static async Task<int> RunAsync(IReadOnlyList<string> arguments, Func<SampleRunner> createRunner,
        TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        if (arguments.Count == 0)
        {
            await output.WriteLineAsync(new Dependency().GetMessage());
            await output.WriteLineAsync("Run with -- <resource> to exercise a local toolbox service. Use --help for names.");
            return 0;
        }
        var resource = arguments[0].ToLowerInvariant();
        if (resource is "--help" or "-h")
        {
            await output.WriteLineAsync(string.Join(Environment.NewLine, ResourceNames));
            await output.WriteLineAsync("validate [resource ...] [--json] [--timeout-seconds 45]");
            await output.WriteLineAsync("No resources selects all 18 local resource checks.");
            return 0;
        }
        if (resource == "validate")
        {
            if (!LiveValidation.TryParse(arguments.Skip(1).ToArray(), out var options, out var message))
            {
                await error.WriteLineAsync(message);
                return 2;
            }
            SampleRunner? runner = null;
            return await LiveValidation.RunAsync(options!, (name, token) =>
                (runner ??= createRunner()).ValidateAsync(name, token), output, cancellationToken);
        }
        if (!ResourceNames.Contains(resource))
        {
            await error.WriteLineAsync($"Unknown resource '{resource}'. Use --help for supported resource names.");
            return 2;
        }
        try
        {
            await output.WriteLineAsync(await createRunner().RunAsync(resource, cancellationToken));
            return 0;
        }
        catch (Exception exception)
        {
            // Provider messages may contain credentials. Report only the exception type.
            await error.WriteLineAsync($"The {resource} sample failed ({exception.GetType().Name}). Check local services and sample configuration.");
            return 1;
        }
    }
}

/// <summary>Shares the CLI's resource dispatch contract with tests using fake providers.</summary>
internal sealed class SampleRunner(IResourceSamples resources, IMessagingSamples messaging, IDatabaseSamples databases,
    ServiceBusValidationSamples? serviceBusValidation = null)
{
    /// <summary>Uses SDK operations and dedicated Service Bus entities for live validation.</summary>
    internal Task<string> ValidateAsync(string resource, CancellationToken cancellationToken) => resource switch
    {
        "servicebus" => (serviceBusValidation ?? throw new InvalidOperationException("Configure the Service Bus validator.")).ValidateQueueAsync(cancellationToken),
        "servicebus-topic" => (serviceBusValidation ?? throw new InvalidOperationException("Configure the Service Bus validator.")).ValidateTopicAsync(cancellationToken),
        _ => RunAsync(resource, cancellationToken)
    };

    /// <summary>Runs one supported example and returns its printable result.</summary>
    internal async Task<string> RunAsync(string resource, CancellationToken cancellationToken)
    {
        return resource switch
        {
            "sqlserver" => JsonSerializer.Serialize(await databases.SqlServerAsync("Hello SQL Server", cancellationToken)),
            "postgres" => JsonSerializer.Serialize(await databases.PostgresAsync("Hello PostgreSQL", cancellationToken)),
            "cosmos" => await resources.CosmosAsync(cancellationToken),
            "dynamodb" => await resources.DynamoDbAsync(cancellationToken),
            "redis" => await resources.RedisAsync(cancellationToken),
            "mail" => await resources.MailAsync(cancellationToken),
            "seq" => await resources.SeqAsync(cancellationToken),
            "wiremock" => await resources.WireMockAsync(cancellationToken),
            "blob" => await resources.BlobAsync(cancellationToken),
            "storage-queue" => await resources.StorageQueueAsync(cancellationToken),
            "table" => await resources.TableAsync(cancellationToken),
            "s3" => await resources.S3Async(cancellationToken),
            "rabbitmq" => await messaging.RabbitMqAsync(cancellationToken),
            "sqs" => await messaging.SqsAsync(cancellationToken),
            "sns" => await messaging.SnsAsync(cancellationToken),
            "eventbridge" => await messaging.EventBridgeAsync(cancellationToken),
            "servicebus" => await messaging.ServiceBusAsync(cancellationToken),
            "servicebus-topic" => await messaging.ServiceBusTopicAsync(cancellationToken),
            _ => throw new InvalidOperationException("Unsupported sample.")
        };
    }
}
