using SaasEngine.Domain.Shared;

namespace SaasEngine.Domain.Identity.Events;

/// <summary>Raised when a user's PII is erased per GDPR Article 17.</summary>
public sealed record UserErasedEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the tenant identifier.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the erased user's identifier.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Gets the erasure event identifier for audit trail linkage.</summary>
    public required Guid ErasureEventId { get; init; }
}
