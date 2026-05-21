namespace SaasEngine.Domain.Shared;

/// <summary>
/// Enforces per-tenant plan limits including seat counts, API rates, and feature flags.
/// Backed by Redis with database fallback.
/// </summary>
public interface IPlanPolicyService
{
    /// <summary>Enforces the seat limit for the specified tenant.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Billing.PlanLimitExceededException">Thrown when the seat count exceeds the plan limit.</exception>
    Task EnforceSeatsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Enforces the API rate limit for the specified tenant and endpoint.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="endpoint">The API endpoint being called.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Billing.PlanLimitExceededException">Thrown when the rate limit is exceeded.</exception>
    Task EnforceApiRateAsync(Guid tenantId, string endpoint, CancellationToken cancellationToken = default);

    /// <summary>Checks whether a feature is enabled for the specified tenant.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="featureKey">The feature flag key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the feature is enabled; otherwise false.</returns>
    Task<bool> IsFeatureEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default);
}
