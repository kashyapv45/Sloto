using System.Data;

namespace SaasEngine.Domain.Shared;

/// <summary>
/// Factory for creating tenant-aware database connections.
/// All Dapper queries should use this factory to obtain connections.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>Creates a new database connection for the current tenant context.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An open database connection scoped to the current tenant.</returns>
    Task<IDbConnection> CreateAsync(CancellationToken cancellationToken = default);
}
