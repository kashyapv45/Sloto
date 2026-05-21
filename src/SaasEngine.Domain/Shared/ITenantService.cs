using SaasEngine.Domain.Billing;

namespace SaasEngine.Domain.Shared;

/// <summary>
/// Provides tenant context for the current request scope.
/// Resolved from JWT claims in HTTP context.
/// </summary>
public interface ITenantService
{
    /// <summary>Gets the tenant identifier for the current request.</summary>
    /// <exception cref="Tenancy.TenantNotResolvedException">Thrown when called outside an HTTP context or when the tenant claim is missing.</exception>
    Guid CurrentTenantId { get; }

    /// <summary>Gets the isolation tier for the current tenant.</summary>
    string CurrentTier { get; }

    /// <summary>Gets the database connection string resolved for the current tenant.</summary>
    /// <returns>A connection string appropriate for the tenant's isolation tier.</returns>
    string GetConnectionString();

    /// <summary>Gets the plan information for the current tenant.</summary>
    /// <returns>The current plan details.</returns>
    PlanInfo GetPlan();

    /// <summary>
    /// Explicitly sets the tenant context for the current scope.
    /// Used by background jobs and event consumers.
    /// </summary>
    void SetTenantContext(Guid tenantId, string tier);
}
