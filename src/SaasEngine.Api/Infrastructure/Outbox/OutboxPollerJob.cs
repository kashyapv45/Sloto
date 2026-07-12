using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MediatR;
using Quartz;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SaasEngine.Api.Infrastructure.Serialization;
using SaasEngine.Api.Infrastructure.Data;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Infrastructure.Outbox;

[DisallowConcurrentExecution]
public sealed class OutboxPollerJob : IJob
{
    private readonly IAdminConnectionFactory _adminDb;
    private readonly IDistributedLock _lockService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxPollerJob> _logger;

    public OutboxPollerJob(
        IAdminConnectionFactory adminDb,
        IDistributedLock lockService,
        IServiceProvider serviceProvider,
        ILogger<OutboxPollerJob> logger)
    {
        _adminDb = adminDb;
        _lockService = lockService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        await ProcessOutboxEventsAsync(context.CancellationToken);
    }

    public async Task ProcessOutboxEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Outbox poller job execution started.");

        IEnumerable<TenantRecord> tenants;
        using (var adminConnection = await _adminDb.CreateAsync(cancellationToken).ConfigureAwait(false))
        {
            tenants = await adminConnection.QueryAsync<TenantRecord>(
                "SELECT id, tier, status FROM tenants").ConfigureAwait(false);
        }

        foreach (var tenant in tenants)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var lockKey = $"outbox:poll:{tenant.Id}";
            using var @lock = await _lockService.AcquireAsync(lockKey, TimeSpan.FromMinutes(2), TimeSpan.Zero, cancellationToken).ConfigureAwait(false);

            if (@lock is null)
            {
                _logger.LogWarning("Could not acquire poller lock for tenant {TenantId}. Skipping poll.", tenant.Id);
                continue;
            }

            try
            {
                await ProcessTenantOutboxAsync(tenant.Id, tenant.Tier, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox events for tenant {TenantId}.", tenant.Id);
            }
        }

        _logger.LogInformation("Outbox poller job execution finished.");
    }

    private async Task ProcessTenantOutboxAsync(Guid tenantId, string tier, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();

        var tenantService = scope.ServiceProvider.GetRequiredService<ITenantService>();
        tenantService.SetTenantContext(tenantId, tier);

        var dbConnectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var connection = await dbConnectionFactory.CreateAsync(ct).ConfigureAwait(false);

        var events = await connection.QueryAsync<OutboxEventRecord>(
            @"SELECT id, event_type as EventType, payload, attempts 
              FROM outbox_events 
              WHERE status = 'pending' AND tenant_id = @TenantId
              ORDER BY created_at ASC 
              LIMIT 50",
            new { TenantId = tenantId }).ConfigureAwait(false);

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        foreach (var outboxEvent in events)
        {
            if (ct.IsCancellationRequested) break;

            object? deserializedEvent = outboxEvent.EventType switch
            {
                "UserRegisteredEvent" => JsonSerializer.Deserialize(outboxEvent.Payload, AppJsonSerializerContext.Default.UserRegisteredEvent),
                _ => null
            };

            if (deserializedEvent is null)
            {
                await connection.ExecuteAsync(
                    "UPDATE outbox_events SET status = 'failed', last_error = 'Unsupported event' WHERE id = @Id",
                    new { Id = outboxEvent.Id }).ConfigureAwait(false);
                continue;
            }

            try
            {
                await mediator.Publish(deserializedEvent, ct).ConfigureAwait(false);
                await connection.ExecuteAsync(
                    "UPDATE outbox_events SET status = 'processed', processed_at = @Now WHERE id = @Id",
                    new { Id = outboxEvent.Id, Now = DateTimeOffset.UtcNow }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing event {Id}.", outboxEvent.Id);
                var newAttempts = outboxEvent.Attempts + 1;
                await connection.ExecuteAsync(
                    "UPDATE outbox_events SET attempts = @Attempts, status = @Status, last_error = @Error WHERE id = @Id",
                    new { Id = outboxEvent.Id, Attempts = newAttempts, Status = newAttempts >= 5 ? "failed" : "pending", Error = ex.Message }).ConfigureAwait(false);
            }
        }
    }
}

public sealed record TenantRecord(Guid Id, string Tier, string Status);

public sealed class OutboxEventRecord
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public int Attempts { get; set; }
}