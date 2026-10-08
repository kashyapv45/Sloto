using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MediatR;
using Microsoft.AspNetCore.Http;
using SaasEngine.Api.Features.Billing;
using SaasEngine.Api.Infrastructure.Serialization;
using SaasEngine.Domain.Audit;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Features.Audit;

/// <summary>
/// MediatR pipeline behavior that automatically records immutable audit logs
/// for requests that implement <see cref="IAuditableRequest"/>.
/// Uses interface-based dispatch instead of runtime reflection for Native AOT compatibility.
/// </summary>
public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IAuditService _auditService;
    private readonly ITenantService _tenantService;
    private readonly IDbConnectionFactory _dbFactory;
    private readonly IAdminConnectionFactory _adminDb;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes a new instance.</summary>
    public AuditBehavior(
        IAuditService auditService,
        ITenantService tenantService,
        IDbConnectionFactory dbFactory,
        IAdminConnectionFactory adminDb,
        IHttpContextAccessor httpContextAccessor)
    {
        _auditService = auditService;
        _tenantService = tenantService;
        _dbFactory = dbFactory;
        _adminDb = adminDb;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // AOT-safe: interface check instead of GetCustomAttribute reflection
        if (request is not IAuditableRequest auditableInfo)
        {
            return await next().ConfigureAwait(false);
        }

        var action = auditableInfo.AuditAction;
        var resourceType = auditableInfo.AuditResourceType;

        // 1. Resolve Tenant Context
        Guid tenantId = Guid.Empty;
        if (request is ITenantAwareRequest tenantAware)
        {
            tenantId = tenantAware.TenantId;
        }
        else
        {
            try
            {
                tenantId = _tenantService.CurrentTenantId;
            }
            catch
            {
                // Fallback to Guid.Empty when tenant context is unavailable
            }
        }

        // 2. Resolve Actor Context
        var httpContext = _httpContextAccessor.HttpContext;
        Guid? actorId = null;
        string? actorEmail = null;

        var actorIdStr = httpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext?.User?.FindFirstValue("sub");

        if (!string.IsNullOrEmpty(actorIdStr) && Guid.TryParse(actorIdStr, out var parsedActorId))
        {
            actorId = parsedActorId;
            
            // Query actor email from tenant db
            try
            {
                using var db = await _dbFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
                actorEmail = await db.QuerySingleOrDefaultAsync<string>(
                    "SELECT email FROM users WHERE id = @Id",
                    new { Id = parsedActorId }).ConfigureAwait(false);
            }
            catch
            {
                // Non-blocking fallback
            }
        }

        // 3. Resolve Target Resource ID (Before execution)
        Guid? resourceId = auditableInfo.ResourceId;

        // 4. Capture Before State
        object? beforeStateObj = null;
        if (resourceId.HasValue && resourceId.Value != Guid.Empty)
        {
            beforeStateObj = await FetchResourceStateAsync(resourceType, resourceId.Value, cancellationToken).ConfigureAwait(false);
        }

        // 5. Execute Request Handler
        var response = await next().ConfigureAwait(false);

        // 6. Resolve Target Resource ID (After execution fallback for creation flows)
        if (!resourceId.HasValue || resourceId.Value == Guid.Empty)
        {
            if (response is IAuditableResponse auditableResponse)
            {
                resourceId = auditableResponse.ResourceId;
            }
        }

        // 7. Capture After State
        object? afterStateObj = null;
        if (resourceId.HasValue && resourceId.Value != Guid.Empty)
        {
            afterStateObj = await FetchResourceStateAsync(resourceType, resourceId.Value, cancellationToken).ConfigureAwait(false);
        }

        // 8. Serialize and compute SHA-256 Hash
        var beforeStateJson = SerializeState(resourceType, beforeStateObj);
        var afterStateJson = SerializeState(resourceType, afterStateObj);
        var payloadHash = ComputeSha256Hash(beforeStateJson, afterStateJson);

        // 9. Record Immutable Audit Event
        var ipAddress = httpContext?.Connection?.RemoteIpAddress?.ToString();
        var userAgent = httpContext?.Request?.Headers["User-Agent"].ToString();

        var auditEvent = new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorId = actorId,
            ActorEmail = actorEmail,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            PayloadHash = payloadHash,
            BeforeState = beforeStateJson,
            AfterState = afterStateJson,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Timestamp = DateTimeOffset.UtcNow
        };

        await _auditService.RecordAsync(auditEvent, cancellationToken).ConfigureAwait(false);

        return response;
    }

    private async Task<object?> FetchResourceStateAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        try
        {
            var lowerType = resourceType.ToLowerInvariant();
            if (lowerType == "tenant")
            {
                using var connection = await _adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);
                return await connection.QuerySingleOrDefaultAsync<SaasEngine.Domain.Tenancy.Tenant>(
                    "SELECT id, name, tier, plan_id as PlanId, status, NULL as ConnectionSecretRef, created_at as CreatedAt, updated_at as UpdatedAt FROM tenants WHERE id = @Id",
                    new { Id = resourceId }).ConfigureAwait(false);
            }
            else if (lowerType == "plan")
            {
                using var connection = await _adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);
                var planRecord = await connection.QuerySingleOrDefaultAsync<PlanDbRecord>(
                    "SELECT id, key, max_seats as MaxSeats, max_api_calls_per_min as MaxApiCallsPerMinute, features, price_monthly_cents as PriceMonthlyCents FROM plans WHERE id = @Id",
                    new { Id = resourceId }).ConfigureAwait(false);

                if (planRecord is null) return null;

                var featuresList = string.IsNullOrEmpty(planRecord.Features)
                    ? new List<string>()
                    : JsonSerializer.Deserialize(planRecord.Features, AppJsonSerializerContext.Default.ListString) ?? new List<string>();

                return new SaasEngine.Domain.Billing.Plan
                {
                    Id = planRecord.Id,
                    Key = planRecord.Key,
                    MaxSeats = planRecord.MaxSeats,
                    MaxApiCallsPerMinute = planRecord.MaxApiCallsPerMinute,
                    Features = featuresList,
                    PriceMonthlyCents = planRecord.PriceMonthlyCents
                };
            }
            else if (lowerType == "user")
            {
                using var connection = await _dbFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
                return await connection.QuerySingleOrDefaultAsync<SaasEngine.Domain.Identity.User>(
                    "SELECT id, tenant_id as TenantId, email, name, role, '' as PasswordHash, NULL as MfaSecret, mfa_enabled as MfaEnabled, status, created_at as CreatedAt FROM users WHERE id = @Id",
                    new { Id = resourceId }).ConfigureAwait(false);
            }
            else if (lowerType == "feature_flag")
            {
                using var connection = await _dbFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
                return await connection.QuerySingleOrDefaultAsync<SaasEngine.Domain.Billing.FeatureFlag>(
                    "SELECT id, tenant_id as TenantId, flag_key as FlagKey, enabled, rollout_percentage as RolloutPercentage, created_at as CreatedAt FROM feature_flags WHERE id = @Id",
                    new { Id = resourceId }).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to fetch state for auditable resource type {ResourceType} and ID {ResourceId}", resourceType, resourceId);
        }
        return null;
    }

    private static string? SerializeState(string resourceType, object? state)
    {
        if (state is null) return null;

        // AOT-safe: explicit type-checked serialization, no reflection fallback
        return resourceType.ToLowerInvariant() switch
        {
            "user" when state is SaasEngine.Domain.Identity.User user =>
                JsonSerializer.Serialize(user, AppJsonSerializerContext.Default.User),
            "tenant" when state is SaasEngine.Domain.Tenancy.Tenant tenant =>
                JsonSerializer.Serialize(tenant, AppJsonSerializerContext.Default.Tenant),
            "plan" when state is SaasEngine.Domain.Billing.Plan plan =>
                JsonSerializer.Serialize(plan, AppJsonSerializerContext.Default.Plan),
            "feature_flag" when state is SaasEngine.Domain.Billing.FeatureFlag flag =>
                JsonSerializer.Serialize(flag, AppJsonSerializerContext.Default.FeatureFlag),
            _ => null // AOT-safe: return null instead of reflection-based object serialization
        };
    }

    private static string ComputeSha256Hash(string? before, string? after)
    {
        var input = $"{before ?? string.Empty}|{after ?? string.Empty}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
