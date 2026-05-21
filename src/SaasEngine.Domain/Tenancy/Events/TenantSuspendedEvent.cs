using SaasEngine.Domain.Shared;

namespace SaasEngine.Domain.Tenancy.Events;

/// <summary>Raised when a tenant is suspended.</summary>
public sealed record TenantSuspendedEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the identifier of the suspended tenant.</summary>
    public required Guid TenantId { get; init; }
}
