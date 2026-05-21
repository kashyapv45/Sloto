using SaasEngine.Domain.Audit;

namespace SaasEngine.Domain.Shared;

/// <summary>
/// Provides immutable audit trail functionality.
/// All write operations create INSERT-only audit records.
/// </summary>
public interface IAuditService
{
    /// <summary>Records an audit event.</summary>
    /// <param name="auditEvent">The audit event to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
