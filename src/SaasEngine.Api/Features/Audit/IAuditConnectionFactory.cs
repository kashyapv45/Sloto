using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SaasEngine.Api.Features.Audit;

/// <summary>
/// Connection factory dedicated to audit logging with restricted privileges.
/// </summary>
public interface IAuditConnectionFactory
{
    /// <summary>Creates a database connection with saas_audit_writer privileges.</summary>
    Task<IDbConnection> CreateAsync(CancellationToken cancellationToken = default);
}
