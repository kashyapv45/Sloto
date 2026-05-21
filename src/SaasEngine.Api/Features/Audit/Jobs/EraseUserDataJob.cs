using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using SaasEngine.Api.Infrastructure.Serialization;
using SaasEngine.Domain.Audit;
using SaasEngine.Domain.Identity;
using SaasEngine.Domain.Identity.Events;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Infrastructure.BackgroundJobs;

/// <summary>
/// Serialized payload for EraseUserDataJob.
/// </summary>
public sealed record EraseUserDataPayload
{
    /// <summary>Gets the tenant identifier.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the user identifier whose data is being erased.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Gets the erasure event identifier for audit linkage.</summary>
    public required Guid ErasureEventId { get; init; }
}

/// <summary>
/// A reflection-free AOT-compatible background job that anonymizes a user's PII per GDPR Article 17,
/// redacts their email from the audit logs, publishes the UserErasedEvent, and logs the completion.
/// </summary>
public sealed class EraseUserDataJob : IBackgroundTaskHandler
{
    private readonly IDbConnectionFactory _dbFactory;
    private readonly IAuditService _auditService;
    private readonly IEventBus _eventBus;

    /// <inheritdoc />
    public string JobType => "EraseUserData";

    /// <summary>Initializes a new instance.</summary>
    public EraseUserDataJob(
        IDbConnectionFactory dbFactory,
        IAuditService auditService,
        IEventBus eventBus)
    {
        _dbFactory = dbFactory;
        _auditService = auditService;
        _eventBus = eventBus;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        var jobPayload = JsonSerializer.Deserialize(payload, AppJsonSerializerContext.Default.EraseUserDataPayload);
        if (jobPayload is null) return;

        using var connection = await _dbFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

        // 1. Anonymize user row
        var erasedEmail = $"erased-{Guid.NewGuid():N}@deleted";
        var rows = await connection.ExecuteAsync(
            @"UPDATE users 
              SET email = @Email, name = @Name, password_hash = '', mfa_secret = NULL, mfa_enabled = 0, status = @Status
              WHERE id = @UserId AND tenant_id = @TenantId",
            new
            {
                Email = erasedEmail,
                Name = "[Deleted User]",
                Status = UserStatus.Erased.ToString(), // "Erased"
                UserId = jobPayload.UserId,
                TenantId = jobPayload.TenantId
            }).ConfigureAwait(false);

        if (rows == 0)
        {
            throw new InvalidOperationException($"User '{jobPayload.UserId}' not found in tenant '{jobPayload.TenantId}'.");
        }

        // 2. Redact actor email from audit_events (safe, only redacts email, not deleting the records)
        await connection.ExecuteAsync(
            "UPDATE audit_events SET actor_email = '[REDACTED]' WHERE actor_id = @UserId AND tenant_id = @TenantId",
            new { UserId = jobPayload.UserId, TenantId = jobPayload.TenantId }).ConfigureAwait(false);

        // 3. Emit UserErasedEvent
        var userErasedEvent = new UserErasedEvent
        {
            TenantId = jobPayload.TenantId,
            UserId = jobPayload.UserId,
            ErasureEventId = jobPayload.ErasureEventId
        };
        await _eventBus.PublishAsync(userErasedEvent, cancellationToken).ConfigureAwait(false);

        // 4. Log ErasureCompleted audit event
        var auditEvent = new AuditEvent
        {
            Id = jobPayload.ErasureEventId,
            TenantId = jobPayload.TenantId,
            ActorId = null,
            ActorEmail = "[SYSTEM]",
            Action = "ErasureCompleted",
            ResourceType = "User",
            ResourceId = jobPayload.UserId,
            PayloadHash = null,
            BeforeState = null,
            AfterState = null,
            IpAddress = null,
            UserAgent = null,
            Timestamp = DateTimeOffset.UtcNow
        };
        await _auditService.RecordAsync(auditEvent, cancellationToken).ConfigureAwait(false);
    }
}
