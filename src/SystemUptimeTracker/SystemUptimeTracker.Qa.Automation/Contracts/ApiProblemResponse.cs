using System.Net;
using System.Text.Json;

namespace SystemUptimeTracker.Qa.Automation.Contracts;

public sealed class ApiProblemResponse
{
    public HttpStatusCode StatusCode { get; init; }

    public string TraceId { get; init; } = string.Empty;

    public JsonDocument Problem { get; init; } = null!;
}