using System.Net;

namespace SystemUptimeTracker.Qa.Automation.Contracts;

public sealed class ApiTextResponse
{
    public HttpStatusCode StatusCode { get; init; }

    public string TraceId { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;
}