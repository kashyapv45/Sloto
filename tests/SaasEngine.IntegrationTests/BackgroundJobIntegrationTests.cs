using System;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaasEngine.Contracts.Identity;
using SaasEngine.Domain.Shared;
using SaasEngine.Api.Infrastructure.Outbox;
using Xunit;

namespace SaasEngine.IntegrationTests;

/// <summary>
/// Integration tests verifying the transactional outbox pattern, outbox poller job, and distributed locks.
/// </summary>
public sealed class BackgroundJobIntegrationTests : IClassFixture<WebApplicationFactory<SaasEngine.Api.Program>>
{
    private readonly WebApplicationFactory<SaasEngine.Api.Program> _factory;
    private readonly MockDbConnectionFactory _dbFactory;

    /// <summary>
    /// Initializes a new instance of BackgroundJobIntegrationTests.
    /// </summary>
    public BackgroundJobIntegrationTests(WebApplicationFactory<SaasEngine.Api.Program> factory)
    {
        _dbFactory = new MockDbConnectionFactory();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace production database factories with SQLite Mock Db
                var adminDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAdminConnectionFactory));
                if (adminDescriptor != null) services.Remove(adminDescriptor);

                var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDbConnectionFactory));
                if (dbDescriptor != null) services.Remove(dbDescriptor);

                services.AddSingleton<IAdminConnectionFactory>(_dbFactory);
                services.AddSingleton<IDbConnectionFactory>(_dbFactory);
            });
        });
    }

    /// <summary>
    /// Verifies that user registration inserts an event to the outbox table and that the outbox poller marks it as processed.
    /// </summary>
    [Fact]
    public async Task Verify_Registration_Enqueues_OutboxEvent_And_Processes()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        // 1. Seed tenant in DB
        using (var connection = await _dbFactory.CreateAsync())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO tenants (id, name, tier, plan_id, status, created_at)
                  VALUES (@Id, 'Job Test Tenant', 'standard', @PlanId, 'active', @CreatedAt)",
                new { Id = tenantId, PlanId = planId, CreatedAt = DateTimeOffset.UtcNow });
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        client.DefaultRequestHeaders.Add("X-Internal-Key", "SaasEngine_DevAdminKey_2026!");

        var registerRequest = new RegisterUserRequest
        {
            Email = "job.user@example.com",
            Name = "Job User",
            Password = "SecurePassword123!",
            Role = "member"
        };

        // 2. Register User (should enqueue outbox event within the same transaction)
        var registerResponse = await client.PostAsJsonAsync("/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        // 3. Verify outbox event is in the database and is pending
        using (var connection = await _dbFactory.CreateAsync())
        {
            var eventRecord = await connection.QuerySingleOrDefaultAsync<OutboxDbRecord>(
                "SELECT id, event_type, status, attempts, processed_at FROM outbox_events WHERE tenant_id = @TenantId",
                new { TenantId = tenantId.ToString() });

            Assert.NotNull(eventRecord);
            Assert.Equal("UserRegisteredEvent", eventRecord.event_type);
            Assert.Equal("pending", eventRecord.status);
            Assert.Equal(0, eventRecord.attempts);
            Assert.Null(eventRecord.processed_at);
        }

        // 4. Run poller manually via the manual outbox endpoint /jobs/outbox/process
        var processResponse = await client.PostAsync("/jobs/outbox/process", null);
        Assert.Equal(HttpStatusCode.OK, processResponse.StatusCode);

        // 5. Verify outbox event status is now updated to processed
        using (var connection = await _dbFactory.CreateAsync())
        {
            var eventRecord = await connection.QuerySingleOrDefaultAsync<OutboxDbRecord>(
                "SELECT id, event_type, status, attempts, processed_at FROM outbox_events WHERE tenant_id = @TenantId",
                new { TenantId = tenantId.ToString() });

            Assert.NotNull(eventRecord);
            Assert.Equal("processed", eventRecord.status);
            Assert.NotNull(eventRecord.processed_at);
        }
    }

    /// <summary>
    /// Verifies that the distributed lock service prevents concurrent access to locked keys.
    /// </summary>
    [Fact]
    public async Task DistributedLock_PreventsConcurrentOutboxPolling()
    {
        var lockService = _factory.Services.GetRequiredService<IDistributedLock>();
        var lockKey = $"test-lock-{Guid.NewGuid()}";

        // Acquire lock
        var handle1 = await lockService.AcquireAsync(lockKey, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        Assert.NotNull(handle1);

        // Try to acquire again - should fail and return null because it's locked
        var handle2 = await lockService.AcquireAsync(lockKey, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        Assert.Null(handle2);

        // Release first lock
        handle1.Dispose();

        // Try to acquire again - should now succeed
        var handle3 = await lockService.AcquireAsync(lockKey, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        Assert.NotNull(handle3);
        handle3.Dispose();
    }

    private sealed class OutboxDbRecord
    {
        public string id { get; set; } = string.Empty;
        public string event_type { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
        public int attempts { get; set; }
        public string? processed_at { get; set; }
    }
}
