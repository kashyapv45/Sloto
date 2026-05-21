using System;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using SaasEngine.Api.Infrastructure.Serialization;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Infrastructure.Outbox;

/// <summary>
/// Service to enqueue outbox events transactionally within a tenant database connection.
/// </summary>
public sealed class OutboxService
{
    private readonly ITenantService _tenantService;

    /// <summary>
    /// Initializes a new instance of OutboxService.
    /// </summary>
    public OutboxService(ITenantService tenantService)
    {
        _tenantService = tenantService;
    }

    /// <summary>
    /// Enqueues an event into the outbox table using the provided connection and transaction.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="connection">The active database connection.</param>
    /// <param name="transaction">The active transaction.</param>
    /// <param name="domainEvent">The event payload to enqueue.</param>
    public async Task EnqueueEventAsync<TEvent>(
        IDbConnection connection, 
        IDbTransaction transaction, 
        TEvent domainEvent) where TEvent : class
    {
        var tenantId = _tenantService.CurrentTenantId;
        var eventType = typeof(TEvent).Name;

        // Serialize using AOT AppJsonSerializerContext
        string payload;
        if (domainEvent is UserRegisteredEvent ure)
        {
            payload = JsonSerializer.Serialize(ure, AppJsonSerializerContext.Default.UserRegisteredEvent);
        }
        else
        {
            throw new NotSupportedException($"Event type {eventType} is not supported for AOT outbox serialization.");
        }

        var sql = @"
            INSERT INTO outbox_events (id, tenant_id, aggregate_id, event_type, payload, status, attempts, created_at)
            VALUES (@Id, @TenantId, @AggregateId, @EventType, @Payload, 'pending', 0, @CreatedAt)";

        var parameters = new
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = tenantId.ToString(),
            AggregateId = (domainEvent as IAggregateEvent)?.AggregateId.ToString(),
            EventType = eventType,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O") // ISO 8601 string representation
        };

        await connection.ExecuteAsync(sql, parameters, transaction).ConfigureAwait(false);
    }
}
