using MediatR;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace SaasEngine.Api.Features.Billing;

/// <summary>
/// Handles <see cref="PlanChangedNotification"/> by busting the cached plan details in Redis.
/// </summary>
public sealed class PlanChangedEventHandler : INotificationHandler<PlanChangedNotification>
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<PlanChangedEventHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="PlanChangedEventHandler"/>.
    /// </summary>
    public PlanChangedEventHandler(
        IConnectionMultiplexer? redis,
        ILogger<PlanChangedEventHandler> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task Handle(PlanChangedNotification notification, CancellationToken cancellationToken)
    {
        if (_redis is not null && _redis.IsConnected)
        {
            var cacheKey = $"saas:plan:{notification.Event.TenantId}";
            try
            {
                var deleted = await _redis.GetDatabase().KeyDeleteAsync(cacheKey).ConfigureAwait(false);
                if (deleted)
                {
                    _logger.LogInformation("Plan cache invalidated for tenant {TenantId} due to plan change.", notification.Event.TenantId);
                }
            }
            catch (RedisException ex)
            {
                _logger.LogError(ex, "Failed to delete plan cache key for tenant {TenantId} on plan change.", notification.Event.TenantId);
            }
        }
    }
}
