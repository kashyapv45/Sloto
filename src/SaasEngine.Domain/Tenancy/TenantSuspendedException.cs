using SaasEngine.Domain.Shared;

namespace SaasEngine.Domain.Tenancy;

/// <summary>
/// Thrown when an operation is attempted on a suspended tenant.
/// </summary>
public sealed class TenantSuspendedException : DomainException
{
    /// <summary>Gets the tenant identifier.</summary>
    public Guid TenantId { get; }

    /// <summary>Initializes a new instance of <see cref="TenantSuspendedException"/>.</summary>
    /// <param name="tenantId">The suspended tenant's identifier.</param>
    public TenantSuspendedException(Guid tenantId)
        : base($"Tenant '{tenantId}' is currently suspended.")
    {
        TenantId = tenantId;
    }
}
