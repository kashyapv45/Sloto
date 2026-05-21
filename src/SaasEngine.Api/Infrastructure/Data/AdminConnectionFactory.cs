using System.Data;
using Npgsql;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Infrastructure.Data;

/// <summary>
/// Creates admin-scoped database connections (not tenant-filtered).
/// Used for tenant lifecycle operations and cross-tenant admin queries.
/// </summary>
public sealed class AdminConnectionFactory : IAdminConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>Initializes a new instance of <see cref="AdminConnectionFactory"/>.</summary>
    /// <param name="connectionString">The admin database connection string.</param>
    public AdminConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<IDbConnection> CreateAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
