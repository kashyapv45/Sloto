using System.Data;
using Npgsql;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Infrastructure.Data;

/// <summary>
/// Creates tenant-aware database connections using Npgsql.
/// Resolves the connection string from the current tenant context.
/// </summary>
public sealed class DapperConnectionFactory : IDbConnectionFactory
{
    private readonly ITenantService _tenantService;

    /// <summary>Initializes a new instance of <see cref="DapperConnectionFactory"/>.</summary>
    /// <param name="tenantService">The tenant service providing connection context.</param>
    public DapperConnectionFactory(ITenantService tenantService)
    {
        _tenantService = tenantService;
    }

    /// <inheritdoc />
    public async Task<IDbConnection> CreateAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = _tenantService.GetConnectionString();
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
