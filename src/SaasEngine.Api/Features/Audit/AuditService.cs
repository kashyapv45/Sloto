using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using SaasEngine.Domain.Audit;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Features.Audit;

/// <summary>
/// Service to log immutable audit events using restricted saas_audit_writer privileges.
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly IAuditConnectionFactory _auditDb;

    /// <summary>Initializes a new instance.</summary>
    public AuditService(IAuditConnectionFactory auditDb)
    {
        _auditDb = auditDb;
    }

    /// <inheritdoc />
    public async Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        using var connection = await _auditDb.CreateAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
            @"INSERT INTO audit_events (id, tenant_id, actor_id, actor_email, action, resource_type, resource_id, payload_hash, before_state, after_state, ip_address, user_agent, ts)
              VALUES (@Id, @TenantId, @ActorId, @ActorEmail, @Action, @ResourceType, @ResourceId, @PayloadHash, @BeforeState, @AfterState, @IpAddress, @UserAgent, @Timestamp)",
            new
            {
                auditEvent.Id,
                auditEvent.TenantId,
                auditEvent.ActorId,
                auditEvent.ActorEmail,
                auditEvent.Action,
                auditEvent.ResourceType,
                auditEvent.ResourceId,
                auditEvent.PayloadHash,
                auditEvent.BeforeState,
                auditEvent.AfterState,
                auditEvent.IpAddress,
                auditEvent.UserAgent,
                Timestamp = auditEvent.Timestamp
            }).ConfigureAwait(false);
    }
}
