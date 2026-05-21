namespace SaasEngine.Domain.Tenancy;

/// <summary>
/// Defines the isolation tier for a tenant.
/// </summary>
public enum TenantTier
{
    /// <summary>Shared database, shared schema with global query filters.</summary>
    Standard = 0,

    /// <summary>Shared database, tenant-schema-per-tenant (PostgreSQL schema isolation).</summary>
    Professional = 1,

    /// <summary>Dedicated database per tenant with encrypted connection strings.</summary>
    Enterprise = 2
}
