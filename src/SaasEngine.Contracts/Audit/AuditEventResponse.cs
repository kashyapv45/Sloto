namespace SaasEngine.Contracts.Audit;

/// <summary>Response containing audit event details.</summary>
public sealed record AuditEventResponse
{
    /// <summary>Gets the event identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the actor identifier.</summary>
    public Guid? ActorId { get; init; }

    /// <summary>Gets the actor email.</summary>
    public string? ActorEmail { get; init; }

    /// <summary>Gets the action performed.</summary>
    public required string Action { get; init; }

    /// <summary>Gets the resource type.</summary>
    public string? ResourceType { get; init; }

    /// <summary>Gets the resource identifier.</summary>
    public Guid? ResourceId { get; init; }

    /// <summary>Gets the event timestamp.</summary>
    public required DateTimeOffset Timestamp { get; init; }
}
