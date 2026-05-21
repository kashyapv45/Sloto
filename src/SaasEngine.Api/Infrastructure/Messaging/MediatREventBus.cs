using MediatR;
using SaasEngine.Api.Features.Billing;
using SaasEngine.Domain.Shared;
using SaasEngine.Domain.Tenancy.Events;

namespace SaasEngine.Api.Infrastructure.Messaging;

/// <summary>
/// In-process event bus implementation using MediatR.
/// For cross-service delivery, events should be written to the outbox.
/// </summary>
public sealed class MediatREventBus : IEventBus
{
    private readonly IMediator _mediator;

    /// <summary>Initializes a new instance of <see cref="MediatREventBus"/>.</summary>
    /// <param name="mediator">The MediatR mediator.</param>
    public MediatREventBus(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        // Wrap known domain events into their MediatR notification counterparts
        INotification? notification = domainEvent switch
        {
            PlanChangedEvent planChanged => new PlanChangedNotification(planChanged),
            INotification n => n,
            _ => null
        };

        if (notification is not null)
        {
            await _mediator.Publish(notification, cancellationToken).ConfigureAwait(false);
        }
    }
}
