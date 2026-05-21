namespace SaasEngine.Domain.Scheduling;

/// <summary>
/// Represents an event in the transactional outbox.
/// </summary>
public sealed record OutboxEvent
{
    /// <summary>Gets the unique event identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant identifier.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the aggregate identifier that triggered this event.</summary>
    public Guid? AggregateId { get; init; }

    /// <summary>Gets the event type name.</summary>
    public required string EventType { get; init; }

    /// <summary>Gets the serialized event payload.</summary>
    public required string Payload { get; init; }

    /// <summary>Gets the processing status.</summary>
    public required OutboxEventStatus Status { get; init; }

    /// <summary>Gets the number of processing attempts.</summary>
    public required int Attempts { get; init; }

    /// <summary>Gets the last error message, if any.</summary>
    public string? LastError { get; init; }

    /// <summary>Gets the creation timestamp.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets the processing timestamp.</summary>
    public DateTimeOffset? ProcessedAt { get; init; }
}

/// <summary>Processing status for outbox events.</summary>
public enum OutboxEventStatus
{
    /// <summary>Event is pending processing.</summary>
    Pending = 0,

    /// <summary>Event has been successfully processed.</summary>
    Processed = 1,

    /// <summary>Event processing failed after maximum attempts.</summary>
    Failed = 2
}
