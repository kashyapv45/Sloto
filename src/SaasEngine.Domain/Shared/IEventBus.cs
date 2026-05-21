namespace SaasEngine.Domain.Shared;

/// <summary>
/// Abstraction for inter-slice communication.
/// In-process delivery via MediatR, with outbox for reliable async delivery.
/// </summary>
public interface IEventBus
{
    /// <summary>Publishes a domain event to all registered handlers.</summary>
    /// <typeparam name="TEvent">The type of domain event.</typeparam>
    /// <param name="domainEvent">The event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;
}

/// <summary>Marker interface for domain events.</summary>
public interface IDomainEvent
{
    /// <summary>Gets the unique identifier for this event occurrence.</summary>
    Guid EventId { get; }

    /// <summary>Gets the UTC timestamp when this event occurred.</summary>
    DateTimeOffset OccurredAt { get; }
}
