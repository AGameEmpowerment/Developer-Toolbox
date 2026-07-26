namespace SystemUptimeTracker.Qa.Automation.Contracts;

public sealed class ApiResponse<T>
{
    public T Payload { get; init; } = default!;

    public string TraceId { get; init; } = string.Empty;
}