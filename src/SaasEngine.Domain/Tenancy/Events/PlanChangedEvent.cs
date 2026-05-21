using SaasEngine.Domain.Shared;

namespace SaasEngine.Domain.Tenancy.Events;

/// <summary>Raised when a tenant's plan is changed.</summary>
public sealed record PlanChangedEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the tenant identifier.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the previous plan identifier.</summary>
    public required Guid OldPlanId { get; init; }

    /// <summary>Gets the new plan identifier.</summary>
    public required Guid NewPlanId { get; init; }
}
