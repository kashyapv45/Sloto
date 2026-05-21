using MediatR;
using SaasEngine.Domain.Tenancy.Events;

namespace SaasEngine.Api.Features.Billing;

/// <summary>
/// MediatR notification wrapper for the domain <see cref="PlanChangedEvent"/>.
/// </summary>
public sealed class PlanChangedNotification : INotification
{
    /// <summary>Gets the underlying domain event.</summary>
    public PlanChangedEvent Event { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="PlanChangedNotification"/>.
    /// </summary>
    /// <param name="event">The domain event.</param>
    public PlanChangedNotification(PlanChangedEvent @event)
    {
        Event = @event;
    }
}
