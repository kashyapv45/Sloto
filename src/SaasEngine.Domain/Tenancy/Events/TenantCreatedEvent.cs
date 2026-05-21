using SaasEngine.Domain.Shared;

namespace SaasEngine.Domain.Tenancy.Events;

/// <summary>Raised when a new tenant is provisioned.</summary>
public sealed record TenantCreatedEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the identifier of the created tenant.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the tenant name.</summary>
    public required string TenantName { get; init; }

    /// <summary>Gets the tenant tier.</summary>
    public required TenantTier Tier { get; init; }
}
