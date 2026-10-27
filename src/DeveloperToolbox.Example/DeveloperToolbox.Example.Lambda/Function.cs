using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using DeveloperToolbox.Example.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace DeveloperToolbox.Example.Lambda;

/// <summary>Local-development Lambda examples for HTTP, EventBridge, and SQS.</summary>
public sealed class Function
{
    private readonly Lazy<IDatabaseSamples> database;

    /// <summary>Uses the reusable EF services in the Lambda execution environment.</summary>
    public Function()
    {
        database = new(() => services.Value.GetRequiredService<IDatabaseSamples>());
    }

    /// <summary>Uses a supplied database service for isolated execution and testing.</summary>
    public Function(IDatabaseSamples database)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = new(() => database);
    }

    // Lambda reuses execution environments, so retain the EF factories between calls.
    private static readonly Lazy<ServiceProvider> services = new(() =>
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        return new ServiceCollection().AddToolboxDatabases(configuration).BuildServiceProvider();
    });

    /// <summary>Validates API Gateway HTTP input and returns a JSON response.</summary>
    public APIGatewayHttpApiV2ProxyResponse HandleHttp(APIGatewayHttpApiV2ProxyRequest? request, ILambdaContext context)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            var encodedBody = request.Body;
            ArgumentException.ThrowIfNullOrWhiteSpace(encodedBody);
            // Bound the encoded body before allocating a decoded buffer or JSON document.
            if (encodedBody.Length > 65536)
            {
                throw new ArgumentException("The request body must contain at most 65536 characters.", nameof(request));
            }
            var body = request.IsBase64Encoded
                ? new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encodedBody))
                : encodedBody;
            var message = ReadMessage(body);
            context.Logger.LogInformation("Processed a local HTTP sample.");
            return new APIGatewayHttpApiV2ProxyResponse
            {
                StatusCode = 200, Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                Body = JsonSerializer.Serialize(new { message })
            };
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException)
        {
            return new APIGatewayHttpApiV2ProxyResponse
            {
                StatusCode = 400, Body = "{\"error\":\"Provide JSON with a nonempty message of at most 4096 characters.\"}"
            };
        }
    }

    /// <summary>Reads an EventBridge envelope's detail payload.</summary>
    public string HandleEvent(JsonElement envelope, ILambdaContext context)
    {
        if (envelope.ValueKind != JsonValueKind.Object || !envelope.TryGetProperty("detail", out var detail))
        {
            throw new ArgumentException("The EventBridge envelope must contain detail.", nameof(envelope));
        }
        var message = ReadMessage(detail.GetRawText());
        context.Logger.LogInformation("Processed a local EventBridge sample.");
        return message;
    }

    /// <summary>Persists valid SQS work in PostgreSQL and reports individual failures for retry.</summary>
    public async Task<SQSBatchResponse> HandleSqs(SQSEvent input, ILambdaContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input.Records);
        var failures = new List<SQSBatchResponse.BatchItemFailure>();
        using var timeout = new CancellationTokenSource();
        // Leave time to return partial failures before Lambda terminates this invocation.
        var budget = context.RemainingTime - TimeSpan.FromSeconds(1);
        if (budget <= TimeSpan.Zero)
        {
            timeout.Cancel();
        }
        else
        {
            timeout.CancelAfter(budget < TimeSpan.FromSeconds(25) ? budget : TimeSpan.FromSeconds(25));
        }
        foreach (var record in input.Records)
        {
            // Missing IDs cannot be expressed in an SQS partial-batch response.
            ArgumentException.ThrowIfNullOrWhiteSpace(record.MessageId);
            try
            {
                timeout.Token.ThrowIfCancellationRequested();
                var message = ReadMessage(record.Body);
                if (record.MessageId.Length > 128)
                {
                    throw new ArgumentException("Sample message IDs permit at most 128 characters.");
                }
                await database.Value.SavePostgresMessageAsync(record.MessageId, message, "aws-sqs", timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                context.Logger.LogInformation("Persisted a local SQS sample.");
            }
            catch (Exception exception)
            {
                // Do not log input payloads, connection strings, or database exception messages.
                context.Logger.LogWarning($"SQS sample failed ({exception.GetType().Name}); it will be retried.");
                failures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = record.MessageId });
            }
        }
        return new SQSBatchResponse(failures);
    }

    /// <summary>Checks the message contract shared by the local Lambda examples.</summary>
    public static string ReadMessage(string? body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("Provide a JSON object with a string message.", nameof(body));
        }
        var text = message.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
        {
            throw new ArgumentException("The message must contain 1 to 4096 characters.", nameof(body));
        }
        return text;
    }
}
