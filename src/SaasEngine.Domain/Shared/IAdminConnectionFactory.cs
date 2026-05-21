using System.Data;

namespace SaasEngine.Domain.Shared;

/// <summary>
/// Factory for creating connection to the admin database.
/// Used for tenant provisioning and management.
/// </summary>
public interface IAdminConnectionFactory
{
    /// <summary>Creates a database connection to the admin database.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An open database connection.</returns>
    Task<IDbConnection> CreateAsync(CancellationToken cancellationToken = default);
}
