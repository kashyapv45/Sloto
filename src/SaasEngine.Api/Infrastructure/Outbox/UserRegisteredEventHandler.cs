using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SaasEngine.Api.Infrastructure.Outbox;

/// <summary>
/// Handler for processing UserRegisteredEvent notifications.
/// </summary>
public sealed class UserRegisteredEventHandler : INotificationHandler<UserRegisteredEvent>
{
    private readonly ILogger<UserRegisteredEventHandler> _logger;

    /// <summary>
    /// Initializes a new instance of UserRegisteredEventHandler.
    /// </summary>
    public UserRegisteredEventHandler(ILogger<UserRegisteredEventHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Successfully handled UserRegisteredEvent in MediatR handler: UserId={UserId}, Email={Email}", 
            notification.UserId, notification.Email);
        return Task.CompletedTask;
    }
}
