namespace SaasEngine.Api.Features.Billing;

/// <summary>
/// Defines a request that explicitly contains a tenant identifier.
/// Useful for unauthenticated flows (e.g., registration) prior to JWT validation.
/// </summary>
public interface ITenantAwareRequest
{
    /// <summary>Gets the tenant identifier.</summary>
    Guid TenantId { get; }
}
