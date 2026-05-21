using SaasEngine.Domain.Shared;

namespace SaasEngine.Domain.Billing;

/// <summary>
/// Thrown when a tenant exceeds their plan's limits (seats, API rate, etc.).
/// </summary>
public sealed class PlanLimitExceededException : DomainException
{
    /// <summary>Gets the tenant identifier.</summary>
    public Guid TenantId { get; }

    /// <summary>Gets the type of limit that was exceeded.</summary>
    public string LimitType { get; }

    /// <summary>Gets the number of seconds until the limit resets, if applicable.</summary>
    public int? RetryAfterSeconds { get; }

    /// <summary>Initializes a new instance of <see cref="PlanLimitExceededException"/>.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="limitType">The type of limit exceeded.</param>
    /// <param name="retryAfterSeconds">Optional retry-after duration in seconds.</param>
    public PlanLimitExceededException(Guid tenantId, string limitType, int? retryAfterSeconds = null)
        : base($"Plan limit '{limitType}' exceeded for tenant '{tenantId}'.")
    {
        TenantId = tenantId;
        LimitType = limitType;
        RetryAfterSeconds = retryAfterSeconds;
    }
}
