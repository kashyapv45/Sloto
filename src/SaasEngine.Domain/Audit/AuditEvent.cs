namespace SaasEngine.Domain.Audit;

/// <summary>
/// Represents an immutable audit event. INSERT-only — no updates or deletes.
/// </summary>
public sealed record AuditEvent
{
    /// <summary>Gets the unique event identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant identifier.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the actor (user) identifier.</summary>
    public Guid? ActorId { get; init; }

    /// <summary>Gets the actor's email address.</summary>
    public string? ActorEmail { get; init; }

    /// <summary>Gets the action performed.</summary>
    public required string Action { get; init; }

    /// <summary>Gets the type of resource affected.</summary>
    public string? ResourceType { get; init; }

    /// <summary>Gets the identifier of the affected resource.</summary>
    public Guid? ResourceId { get; init; }

    /// <summary>Gets the SHA-256 hash of the serialized diff.</summary>
    public string? PayloadHash { get; init; }

    /// <summary>Gets the state before the change (JSONB).</summary>
    public string? BeforeState { get; init; }

    /// <summary>Gets the state after the change (JSONB).</summary>
    public string? AfterState { get; init; }

    /// <summary>Gets the client IP address.</summary>
    public string? IpAddress { get; init; }

    /// <summary>Gets the client user agent.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Gets the UTC timestamp of the event.</summary>
    public required DateTimeOffset Timestamp { get; init; }
}
