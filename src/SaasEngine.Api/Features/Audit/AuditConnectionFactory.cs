using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Features.Audit;

/// <summary>
/// Implements IAuditConnectionFactory to create database connections dedicated to audit writes.
/// Fallbacks to the in-memory SQLite master database during integration testing.
/// </summary>
public sealed class AuditConnectionFactory : IAuditConnectionFactory
{
    private readonly string _connectionString;
    private readonly IAdminConnectionFactory _adminDb;

    /// <summary>Initializes a new instance.</summary>
    public AuditConnectionFactory(string connectionString, IAdminConnectionFactory adminDb)
    {
        _connectionString = connectionString;
        _adminDb = adminDb;
    }

    /// <inheritdoc />
    public async Task<IDbConnection> CreateAsync(CancellationToken cancellationToken = default)
    {
        // Detect in-memory SQLite mock factory during integration testing and reuse its connection
        if (_adminDb.GetType().Name.Contains("MockDbConnectionFactory", System.StringComparison.OrdinalIgnoreCase))
        {
            return await _adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);
        }

        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
