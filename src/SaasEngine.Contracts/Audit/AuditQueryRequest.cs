namespace SaasEngine.Contracts.Audit;

/// <summary>Request to query audit events with filters.</summary>
public sealed record AuditQueryRequest
{
    /// <summary>Gets the start date filter.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Gets the end date filter.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Gets the action filter.</summary>
    public string? Action { get; init; }

    /// <summary>Gets the resource type filter.</summary>
    public string? ResourceType { get; init; }

    /// <summary>Gets the actor identifier filter.</summary>
    public Guid? ActorId { get; init; }

    /// <summary>Gets the cursor for pagination.</summary>
    public string? Cursor { get; init; }

    /// <summary>Gets the page size (default 50, max 100).</summary>
    public int PageSize { get; init; } = 50;
}
