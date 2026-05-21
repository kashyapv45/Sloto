using SaasEngine.Domain.Shared;

namespace SaasEngine.Domain.Tenancy;

/// <summary>
/// Thrown when a tenant cannot be found by its identifier.
/// </summary>
public sealed class TenantNotFoundException : DomainException
{
    /// <summary>Gets the tenant identifier that was not found.</summary>
    public Guid TenantId { get; }

    /// <summary>Initializes a new instance of <see cref="TenantNotFoundException"/>.</summary>
    /// <param name="tenantId">The tenant identifier that was not found.</param>
    public TenantNotFoundException(Guid tenantId)
        : base($"Tenant '{tenantId}' was not found.")
    {
        TenantId = tenantId;
    }
}
