using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Hangfire;
using SaasEngine.Api.Infrastructure.BackgroundJobs;
using SaasEngine.Api.Infrastructure.Serialization;
using SaasEngine.Contracts.Audit;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Features.Audit.Endpoints;

/// <summary>
/// Defines minimum API endpoints for the Audit query and pipeline management.
/// </summary>
public static class AuditEndpoints
{
    /// <summary>Maps audit-related minimal API endpoints.</summary>
    public static void MapAuditEndpoints(this WebApplication app)
    {
        // Admin-level audit query API
        app.MapGet("/admin/audit", GetAuditEvents)
            .WithTags("Audit");

        // Admin-level tenant data export API
        app.MapPost("/admin/tenants/{id:guid}/export", ExportTenant)
            .WithTags("Tenants");

        // Tenant-level GDPR user PII erasure API
        app.MapPost("/tenants/{id:guid}/users/{userId:guid}/erase", EraseUser)
            .WithTags("Identity");
    }

    private static async Task<IResult> GetAuditEvents(
        [AsParameters] AuditQueryRequest request,
        [FromQuery] Guid? tenantId,
        [FromServices] IAdminConnectionFactory adminDb,
        CancellationToken cancellationToken)
    {
        using var connection = await adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);

        var sql = new StringBuilder(@"
            SELECT id as Id, tenant_id as TenantId, actor_id as ActorId, actor_email as ActorEmail, 
                   action as Action, resource_type as ResourceType, resource_id as ResourceId, 
                   payload_hash as PayloadHash, before_state as BeforeState, after_state as AfterState, 
                   ip_address as IpAddress, user_agent as UserAgent, ts as Timestamp 
            FROM audit_events 
            WHERE 1=1");

        var countSql = new StringBuilder("SELECT COUNT(*) FROM audit_events WHERE 1=1");
        
        var parameters = new DynamicParameters();

        if (tenantId.HasValue)
        {
            sql.Append(" AND tenant_id = @TenantId");
            countSql.Append(" AND tenant_id = @TenantId");
            parameters.Add("TenantId", tenantId.Value.ToString());
        }

        if (request.From.HasValue)
        {
            sql.Append(" AND ts >= @From");
            countSql.Append(" AND ts >= @From");
            parameters.Add("From", request.From.Value);
        }

        if (request.To.HasValue)
        {
            sql.Append(" AND ts <= @To");
            countSql.Append(" AND ts <= @To");
            parameters.Add("To", request.To.Value);
        }

        if (!string.IsNullOrEmpty(request.Action))
        {
            sql.Append(" AND action = @Action");
            countSql.Append(" AND action = @Action");
            parameters.Add("Action", request.Action);
        }

        if (!string.IsNullOrEmpty(request.ResourceType))
        {
            sql.Append(" AND resource_type = @ResourceType");
            countSql.Append(" AND resource_type = @ResourceType");
            parameters.Add("ResourceType", request.ResourceType);
        }

        if (request.ActorId.HasValue)
        {
            sql.Append(" AND actor_id = @ActorId");
            countSql.Append(" AND actor_id = @ActorId");
            parameters.Add("ActorId", request.ActorId.Value.ToString());
        }

        if (!string.IsNullOrEmpty(request.Cursor))
        {
            try
            {
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(request.Cursor)).Split('|');
                if (parts.Length == 2 && DateTimeOffset.TryParse(parts[0], out var cursorTime) && Guid.TryParse(parts[1], out var cursorId))
                {
                    sql.Append(" AND (ts < @CursorTime OR (ts = @CursorTime AND id < @CursorId))");
                    parameters.Add("CursorTime", cursorTime);
                    parameters.Add("CursorId", cursorId.ToString());
                }
            }
            catch
            {
                // Ignore malformed cursor
            }
        }

        // Limit is pageSize + 1 so we know if there is a next page
        var limit = Math.Clamp(request.PageSize, 1, 100);
        sql.Append(" ORDER BY ts DESC, id DESC LIMIT @Limit");
        parameters.Add("Limit", limit + 1);

        var dbResults = await connection.QueryAsync<SaasEngine.Domain.Audit.AuditEvent>(sql.ToString(), parameters).ConfigureAwait(false);
        var items = new List<SaasEngine.Domain.Audit.AuditEvent>(dbResults);

        bool hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        var responses = new List<AuditEventResponse>();
        foreach (var item in items)
        {
            responses.Add(new AuditEventResponse
            {
                Id = item.Id,
                ActorId = item.ActorId,
                ActorEmail = item.ActorEmail,
                Action = item.Action,
                ResourceType = item.ResourceType,
                ResourceId = item.ResourceId,
                Timestamp = item.Timestamp
            });
        }

        string? nextCursor = null;
        if (hasMore && responses.Count > 0)
        {
            var last = responses[^1];
            nextCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{last.Timestamp:O}|{last.Id}"));
        }

        var pagedResult = new PagedResult<AuditEventResponse>
        {
            Items = responses,
            NextCursor = nextCursor
        };

        return TypedResults.Json(pagedResult, AppJsonSerializerContext.Default.PagedResultAuditEventResponse);
    }

    private static IResult ExportTenant(
        Guid id,
        [FromServices] Hangfire.IBackgroundJobClient jobClient,
        CancellationToken cancellationToken)
    {
        var exportEventId = Guid.NewGuid();
        var payload = new ExportTenantDataPayload
        {
            TenantId = id,
            ExportEventId = exportEventId
        };

        var payloadStr = JsonSerializer.Serialize(payload, AppJsonSerializerContext.Default.ExportTenantDataPayload);

        jobClient.Enqueue<BackgroundTaskDispatcher>(
            dispatcher => dispatcher.DispatchAsync("ExportTenantData", payloadStr, id, CancellationToken.None));

        return Results.Accepted(value: new { ExportEventId = exportEventId });
    }

    private static IResult EraseUser(
        Guid id,
        Guid userId,
        [FromServices] Hangfire.IBackgroundJobClient jobClient,
        CancellationToken cancellationToken)
    {
        var erasureEventId = Guid.NewGuid();
        var payload = new EraseUserDataPayload
        {
            TenantId = id,
            UserId = userId,
            ErasureEventId = erasureEventId
        };

        var payloadStr = JsonSerializer.Serialize(payload, AppJsonSerializerContext.Default.EraseUserDataPayload);

        jobClient.Enqueue<BackgroundTaskDispatcher>(
            dispatcher => dispatcher.DispatchAsync("EraseUserData", payloadStr, id, CancellationToken.None));

        return Results.Accepted(value: new { ErasureEventId = erasureEventId });
    }
}
