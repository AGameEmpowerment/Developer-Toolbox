namespace DeveloperToolbox.Example.Data;

/// <summary>A small durable work item shared by the database and queue examples.</summary>
public sealed class SampleMessage
{
    /// <summary>The stable message ID makes queue redelivery idempotent.</summary>
    public required string Id { get; init; }

    /// <summary>The bounded example payload.</summary>
    public required string Text { get; init; }

    /// <summary>The sample that produced this row.</summary>
    public required string Source { get; init; }

    /// <summary>The UTC time when the row was first persisted.</summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>The inserted item and the latest ten rows read with EF Core.</summary>
public sealed record DatabaseSampleResult(string Provider, SampleMessage Inserted, IReadOnlyList<SampleMessage> Recent);
