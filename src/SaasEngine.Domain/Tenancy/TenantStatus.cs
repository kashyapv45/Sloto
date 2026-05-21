namespace SaasEngine.Domain.Tenancy;

/// <summary>
/// Defines the lifecycle status of a tenant.
/// </summary>
public enum TenantStatus
{
    /// <summary>Tenant is active and can make API requests.</summary>
    Active = 0,

    /// <summary>Tenant is suspended and all API access is blocked.</summary>
    Suspended = 1,

    /// <summary>Tenant has been soft-deleted and is pending purge.</summary>
    Deleted = 2
}
