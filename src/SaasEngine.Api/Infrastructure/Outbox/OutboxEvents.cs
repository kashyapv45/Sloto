using System;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Infrastructure.Outbox;

/// <summary>
/// Marker interface for outbox aggregate events.
/// </summary>
public interface IAggregateEvent
{
    /// <summary>
    /// Gets the unique identifier for the aggregate root related to this event.
    /// </summary>
    Guid AggregateId { get; }
}

/// <summary>
/// Event published when a user registers successfully.
/// </summary>
public sealed record UserRegisteredEvent(
    Guid UserId, 
    string Email, 
    string Name, 
    string Role, 
    Guid EventId, 
    DateTimeOffset OccurredAt) : IAggregateEvent, IDomainEvent, MediatR.INotification
{
    /// <inheritdoc />
    public Guid AggregateId => UserId;
}
