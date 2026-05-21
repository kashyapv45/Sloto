namespace SaasEngine.Api.Features.Tenancy;

/// <summary>
/// Resolves the database connection string based on tenant tier.
/// </summary>
public interface ITenantConnectionResolver
{
    /// <summary>Resolves the connection string for the given tenant.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="tier">The tenant's isolation tier.</param>
    /// <returns>The resolved connection string.</returns>
    string Resolve(Guid tenantId, string tier);
}
