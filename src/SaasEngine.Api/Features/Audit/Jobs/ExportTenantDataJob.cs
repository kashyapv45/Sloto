using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using SaasEngine.Api.Infrastructure.Serialization;
using SaasEngine.Api.Features.Audit;
using SaasEngine.Domain.Audit;
using SaasEngine.Domain.Shared;
using SaasEngine.Domain.Tenancy.Events;

namespace SaasEngine.Api.Infrastructure.BackgroundJobs;

/// <summary>
/// Serialized payload for ExportTenantDataJob.
/// </summary>
public sealed record ExportTenantDataPayload
{
    /// <summary>Gets the tenant identifier to export.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the export event identifier for audit linkage.</summary>
    public required Guid ExportEventId { get; init; }
}

/// <summary>
/// A reflection-free AOT-compatible background job that extracts a tenant's complete dataset,
/// packages it in a ZIP archive, publishes a completion event, and records an audit log.
/// </summary>
public sealed class ExportTenantDataJob : IBackgroundTaskHandler
{
    private readonly IAdminConnectionFactory _adminDb;
    private readonly IDbConnectionFactory _dbFactory;
    private readonly IAuditConnectionFactory _auditDb;
    private readonly IAuditService _auditService;
    private readonly IEventBus _eventBus;

    /// <inheritdoc />
    public string JobType => "ExportTenantData";

    /// <summary>Initializes a new instance.</summary>
    public ExportTenantDataJob(
        IAdminConnectionFactory adminDb,
        IDbConnectionFactory dbFactory,
        IAuditConnectionFactory auditDb,
        IAuditService auditService,
        IEventBus eventBus)
    {
        _adminDb = adminDb;
        _dbFactory = dbFactory;
        _auditDb = auditDb;
        _auditService = auditService;
        _eventBus = eventBus;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        var jobPayload = JsonSerializer.Deserialize(payload, AppJsonSerializerContext.Default.ExportTenantDataPayload);
        if (jobPayload is null) return;

        var tenantId = jobPayload.TenantId;

        // 1. Fetch Tenant and Plan from Admin DB
        SaasEngine.Domain.Tenancy.Tenant? tenant = null;
        SaasEngine.Domain.Billing.Plan? plan = null;

        using (var connection = await _adminDb.CreateAsync(cancellationToken).ConfigureAwait(false))
        {
            tenant = await connection.QuerySingleOrDefaultAsync<SaasEngine.Domain.Tenancy.Tenant>(
                "SELECT id, name, tier, plan_id as PlanId, status, connection_secret_ref as ConnectionSecretRef, created_at as CreatedAt, updated_at as UpdatedAt FROM tenants WHERE id = @Id",
                new { Id = tenantId }).ConfigureAwait(false);

            if (tenant is not null)
            {
                var planRecord = await connection.QuerySingleOrDefaultAsync<PlanDbRecord>(
                    "SELECT id, key, max_seats as MaxSeats, max_api_calls_per_min as MaxApiCallsPerMinute, features, price_monthly_cents as PriceMonthlyCents FROM plans WHERE id = @Id",
                    new { Id = tenant.PlanId }).ConfigureAwait(false);

                if (planRecord is not null)
                {
                    var featuresList = string.IsNullOrEmpty(planRecord.Features)
                        ? new List<string>()
                        : JsonSerializer.Deserialize(planRecord.Features, AppJsonSerializerContext.Default.ListString) ?? new List<string>();

                    plan = new SaasEngine.Domain.Billing.Plan
                    {
                        Id = planRecord.Id,
                        Key = planRecord.Key,
                        MaxSeats = planRecord.MaxSeats,
                        MaxApiCallsPerMinute = planRecord.MaxApiCallsPerMinute,
                        Features = featuresList,
                        PriceMonthlyCents = planRecord.PriceMonthlyCents
                    };
                }
            }
        }

        if (tenant is null)
        {
            throw new InvalidOperationException($"Tenant '{tenantId}' was not found.");
        }

        // 2. Fetch Users and Feature Flags from Tenant DB
        List<SaasEngine.Domain.Identity.User> users = new();
        List<SaasEngine.Domain.Billing.FeatureFlag> featureFlags = new();

        using (var connection = await _dbFactory.CreateAsync(cancellationToken).ConfigureAwait(false))
        {
            var userResults = await connection.QueryAsync<SaasEngine.Domain.Identity.User>(
                "SELECT id, tenant_id as TenantId, email, name, role, password_hash as PasswordHash, mfa_secret as MfaSecret, mfa_enabled as MfaEnabled, status, created_at as CreatedAt FROM users WHERE tenant_id = @TenantId",
                new { TenantId = tenantId }).ConfigureAwait(false);
            users.AddRange(userResults);

            var flagResults = await connection.QueryAsync<SaasEngine.Domain.Billing.FeatureFlag>(
                "SELECT id, tenant_id as TenantId, flag_key as FlagKey, enabled, rollout_percentage as RolloutPercentage, created_at as CreatedAt FROM feature_flags WHERE tenant_id = @TenantId",
                new { TenantId = tenantId }).ConfigureAwait(false);
            featureFlags.AddRange(flagResults);
        }

        // 3. Fetch Audit Events from Audit DB
        List<SaasEngine.Domain.Audit.AuditEvent> auditEvents = new();
        using (var connection = await _auditDb.CreateAsync(cancellationToken).ConfigureAwait(false))
        {
            var auditResults = await connection.QueryAsync<SaasEngine.Domain.Audit.AuditEvent>(
                "SELECT id as Id, tenant_id as TenantId, actor_id as ActorId, actor_email as ActorEmail, action as Action, resource_type as ResourceType, resource_id as ResourceId, payload_hash as PayloadHash, before_state as BeforeState, after_state as AfterState, ip_address as IpAddress, user_agent as UserAgent, ts as Timestamp FROM audit_events WHERE tenant_id = @TenantId",
                new { TenantId = tenantId }).ConfigureAwait(false);
            auditEvents.AddRange(auditResults);
        }

        // 4. Create ZIP in temp folder
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "temp_exports");
        Directory.CreateDirectory(tempDir);
        var zipPath = Path.Combine(tempDir, $"tenant_export_{tenantId}_{Guid.NewGuid():N}.zip");

        using (var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            // Tenant
            var tenantEntry = archive.CreateEntry("tenant.json");
            using (var writer = new StreamWriter(tenantEntry.Open()))
            {
                writer.Write(JsonSerializer.Serialize(tenant, AppJsonSerializerContext.Default.Tenant));
            }

            // Plan
            if (plan is not null)
            {
                var planEntry = archive.CreateEntry("plan.json");
                using (var writer = new StreamWriter(planEntry.Open()))
                {
                    writer.Write(JsonSerializer.Serialize(plan, AppJsonSerializerContext.Default.Plan));
                }
            }

            // Users
            var usersEntry = archive.CreateEntry("users.json");
            using (var writer = new StreamWriter(usersEntry.Open()))
            {
                writer.Write(JsonSerializer.Serialize(users, AppJsonSerializerContext.Default.ListUser));
            }

            // Feature Flags
            var flagsEntry = archive.CreateEntry("feature_flags.json");
            using (var writer = new StreamWriter(flagsEntry.Open()))
            {
                writer.Write(JsonSerializer.Serialize(featureFlags, AppJsonSerializerContext.Default.ListFeatureFlag));
            }

            // Audit Events
            var auditEntry = archive.CreateEntry("audit_events.json");
            using (var writer = new StreamWriter(auditEntry.Open()))
            {
                writer.Write(JsonSerializer.Serialize(auditEvents, AppJsonSerializerContext.Default.ListAuditEvent));
            }
        }

        // 5. Get file size
        var totalBytes = new FileInfo(zipPath).Length;

        // 6. Publish TenantExportCompletedEvent
        var exportEvent = new TenantExportCompletedEvent
        {
            TenantId = tenantId,
            ZipPath = zipPath,
            TotalBytes = totalBytes,
            ExportEventId = jobPayload.ExportEventId
        };
        await _eventBus.PublishAsync(exportEvent, cancellationToken).ConfigureAwait(false);

        // 7. Log TenantExportCompleted audit event
        var auditEvent = new AuditEvent
        {
            Id = jobPayload.ExportEventId,
            TenantId = tenantId,
            ActorId = null,
            ActorEmail = "[SYSTEM]",
            Action = "TenantExportCompleted",
            ResourceType = "Tenant",
            ResourceId = tenantId,
            PayloadHash = null,
            BeforeState = null,
            AfterState = null,
            IpAddress = null,
            UserAgent = null,
            Timestamp = DateTimeOffset.UtcNow
        };
        await _auditService.RecordAsync(auditEvent, cancellationToken).ConfigureAwait(false);
    }

    private sealed record PlanDbRecord
    {
        public required Guid Id { get; init; }
        public required string Key { get; init; }
        public required int MaxSeats { get; init; }
        public required int MaxApiCallsPerMinute { get; init; }
        public required string Features { get; init; }
        public required int PriceMonthlyCents { get; init; }
    }
}
