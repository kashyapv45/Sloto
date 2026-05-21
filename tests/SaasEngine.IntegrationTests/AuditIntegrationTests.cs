using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SaasEngine.Api.Features.Audit;
using SaasEngine.Api.Features.Identity;
using SaasEngine.Api.Infrastructure.BackgroundJobs;
using SaasEngine.Api.Infrastructure.Serialization;
using SaasEngine.Contracts.Audit;
using SaasEngine.Contracts.Identity;
using SaasEngine.Domain.Audit;
using SaasEngine.Domain.Identity.Events;
using SaasEngine.Domain.Shared;
using SaasEngine.Domain.Tenancy.Events;
using Xunit;

namespace SaasEngine.IntegrationTests;

public sealed class AuditIntegrationTests : IClassFixture<WebApplicationFactory<SaasEngine.Api.Program>>
{
    private readonly WebApplicationFactory<SaasEngine.Api.Program> _factory;
    private readonly MockDbConnectionFactory _dbFactory;
    private readonly IEventBus _mockEventBus;

    public AuditIntegrationTests(WebApplicationFactory<SaasEngine.Api.Program> factory)
    {
        _dbFactory = new MockDbConnectionFactory();
        _mockEventBus = Substitute.For<IEventBus>();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove production DB factories
                var adminDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IAdminConnectionFactory));
                if (adminDescriptor != null) services.Remove(adminDescriptor);

                var dbDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IDbConnectionFactory));
                if (dbDescriptor != null) services.Remove(dbDescriptor);

                var auditDbDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IAuditConnectionFactory));
                if (auditDbDescriptor != null) services.Remove(auditDbDescriptor);

                // Register SQLite Mock Connection Factory
                services.AddSingleton<IAdminConnectionFactory>(_dbFactory);
                services.AddSingleton<IDbConnectionFactory>(_dbFactory);
                services.AddSingleton<IAuditConnectionFactory>(
                    sp => new AuditConnectionFactory("Data Source=:memory:", _dbFactory));

                // Replace IEventBus with NSubstitute mock
                var eventBusDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IEventBus));
                if (eventBusDescriptor != null) services.Remove(eventBusDescriptor);
                services.AddSingleton<IEventBus>(_mockEventBus);
            });
        });
    }

    [Fact]
    public async Task AuditBehavior_ShouldRecord_AuditEvent_When_AuditableCommand_Runs()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        // 1. Seed Tenant in DB
        using (var connection = await _dbFactory.CreateAsync())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO tenants (id, name, tier, plan_id, status, created_at)
                  VALUES (@Id, 'Audit Test Tenant', 'standard', @PlanId, 'active', @Now)",
                new { Id = tenantId, PlanId = planId, Now = DateTimeOffset.UtcNow });
        }

        var client = _factory.CreateClient();

        // 2. Register User (This executes RegisterUserCommand decorated with [Auditable("Register", "User")])
        var registerRequest = new RegisterUserRequest
        {
            Email = "audit_user@saasengine.com",
            Name = "Auditable User",
            Password = "SecurePassword123!",
            Role = "admin"
        };

        var registerMsg = new HttpRequestMessage(HttpMethod.Post, "/auth/register")
        {
            Content = JsonContent.Create(registerRequest)
        };
        registerMsg.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var registerResponse = await client.SendAsync(registerMsg);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        // 3. Query audit_events to assert logging succeeded
        using (var connection = await _dbFactory.CreateAsync())
        {
            var auditEvents = (await connection.QueryAsync<AuditEvent>(
                "SELECT id as Id, tenant_id as TenantId, actor_id as ActorId, actor_email as ActorEmail, action as Action, resource_type as ResourceType, resource_id as ResourceId, payload_hash as PayloadHash, before_state as BeforeState, after_state as AfterState, ts as Timestamp FROM audit_events WHERE tenant_id = @TenantId",
                new { TenantId = tenantId })).ToList();

            Assert.Single(auditEvents);
            var audit = auditEvents[0];
            Assert.Equal("Register", audit.Action);
            Assert.Equal("User", audit.ResourceType);
            Assert.Null(audit.BeforeState);
            Assert.NotNull(audit.AfterState);
            Assert.Contains("audit_user@saasengine.com", audit.AfterState);
            Assert.NotNull(audit.PayloadHash);
        }
    }

    [Fact]
    public async Task GetAuditEvents_ShouldReturn_PagedResults_With_Cursor()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // 1. Seed multiple audit events into DB with strict ordering timestamps
        using (var connection = await _dbFactory.CreateAsync())
        {
            for (int i = 1; i <= 5; i++)
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO audit_events (id, tenant_id, action, resource_type, ts)
                      VALUES (@Id, @TenantId, @Action, 'TestResource', @Ts)",
                    new
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        Action = $"Action_{i}",
                        Ts = now.AddMinutes(i)
                    });
            }
        }

        var client = _factory.CreateClient();

        // 2. Query Page 1 with pageSize = 2 (should return Action_5, Action_4)
        var response1 = await client.GetAsync($"/admin/audit?tenantId={tenantId}&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var page1 = await response1.Content.ReadFromJsonAsync<PagedResult<AuditEventResponse>>();
        Assert.NotNull(page1);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal("Action_5", page1.Items[0].Action);
        Assert.Equal("Action_4", page1.Items[1].Action);
        Assert.NotNull(page1.NextCursor);
        Assert.True(page1.HasMore);

        // 3. Query Page 2 using NextCursor (should return Action_3, Action_2)
        var response2 = await client.GetAsync($"/admin/audit?tenantId={tenantId}&pageSize=2&cursor={Uri.EscapeDataString(page1.NextCursor)}");
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        var page2 = await response2.Content.ReadFromJsonAsync<PagedResult<AuditEventResponse>>();
        Assert.NotNull(page2);
        Assert.Equal(2, page2.Items.Count);
        Assert.Equal("Action_3", page2.Items[0].Action);
        Assert.Equal("Action_2", page2.Items[1].Action);
        Assert.NotNull(page2.NextCursor);
        Assert.True(page2.HasMore);

        // 4. Query Page 3 using Page 2 NextCursor (should return Action_1)
        var response3 = await client.GetAsync($"/admin/audit?tenantId={tenantId}&pageSize=2&cursor={Uri.EscapeDataString(page2.NextCursor)}");
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);

        var page3 = await response3.Content.ReadFromJsonAsync<PagedResult<AuditEventResponse>>();
        Assert.NotNull(page3);
        Assert.Single(page3.Items);
        Assert.Equal("Action_1", page3.Items[0].Action);
        Assert.Null(page3.NextCursor);
        Assert.False(page3.HasMore);
    }

    [Fact]
    public async Task EraseUserDataJob_ShouldAnonymizeUser_RedactAudits_And_PublishEvent()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var erasureEventId = Guid.NewGuid();

        // 1. Seed user and audit events in DB
        using (var connection = await _dbFactory.CreateAsync())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO users (id, tenant_id, email, name, role, status, created_at)
                  VALUES (@Id, @TenantId, 'pii_user@test.com', 'PII User Name', 'user', 'active', @Now)",
                new { Id = userId, TenantId = tenantId, Now = DateTimeOffset.UtcNow });

            await connection.ExecuteAsync(
                @"INSERT INTO audit_events (id, tenant_id, actor_id, actor_email, action, ts)
                  VALUES (@Id, @TenantId, @ActorId, 'pii_user@test.com', 'CreateReport', @Now)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, ActorId = userId, Now = DateTimeOffset.UtcNow });
        }

        // 2. Resolve job from DI container and execute synchronously
        using var scope = _factory.Services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IBackgroundTaskHandler>();
        var eraseJob = handlers.First(h => h.JobType == "EraseUserData");

        var payload = new EraseUserDataPayload
        {
            TenantId = tenantId,
            UserId = userId,
            ErasureEventId = erasureEventId
        };
        var payloadStr = JsonSerializer.Serialize(payload, AppJsonSerializerContext.Default.EraseUserDataPayload);

        await eraseJob.ExecuteAsync(payloadStr, CancellationToken.None);

        // 3. Verify Database updates
        using (var connection = await _dbFactory.CreateAsync())
        {
            // Verify User anonymization
            var user = await connection.QuerySingleAsync<dynamic>(
                "SELECT email, name, password_hash, mfa_secret, mfa_enabled, status FROM users WHERE id = @Id",
                new { Id = userId });
            Assert.StartsWith("erased-", (string)user.email);
            Assert.EndsWith("@deleted", (string)user.email);
            Assert.Equal("[Deleted User]", (string)user.name);
            Assert.Equal("", (string)user.password_hash);
            Assert.Null((string)user.mfa_secret);
            Assert.Equal(0, (long)user.mfa_enabled);
            Assert.Equal("Erased", (string)user.status);

            // Verify Audit events redaction
            var auditEmail = await connection.QuerySingleAsync<string>(
                "SELECT actor_email FROM audit_events WHERE actor_id = @UserId AND action = 'CreateReport'",
                new { UserId = userId });
            Assert.Equal("[REDACTED]", auditEmail);

            // Verify ErasureCompleted audit event was recorded
            var erasureAudit = await connection.QuerySingleAsync<dynamic>(
                "SELECT action, resource_type, resource_id FROM audit_events WHERE id = @Id",
                new { Id = erasureEventId });
                        Assert.Equal("ErasureCompleted", (string)erasureAudit.action);
            Assert.Equal("User", (string)erasureAudit.resource_type);
            Assert.Equal(userId, Guid.Parse((string)erasureAudit.resource_id));
        }

        // 4. Verify Event Bus publishing
        await _mockEventBus.Received(1).PublishAsync(
            Arg.Is<UserErasedEvent>(e => e.TenantId == tenantId && e.UserId == userId && e.ErasureEventId == erasureEventId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportTenantDataJob_ShouldExtractData_CreateZip_And_PublishEvent()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var exportEventId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // 1. Seed Plan, Tenant, User, Feature Flag, and Audit Event
        using (var connection = await _dbFactory.CreateAsync())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO plans (id, key, max_seats, max_api_calls_per_min, features, price_monthly_cents)
                  VALUES (@Id, 'export-plan', 10, 100, '[]', 2000)",
                new { Id = planId });

            await connection.ExecuteAsync(
                @"INSERT INTO tenants (id, name, tier, plan_id, status, created_at)
                  VALUES (@Id, 'Export Tenant', 'standard', @PlanId, 'active', @Now)",
                new { Id = tenantId, PlanId = planId, Now = now });

            await connection.ExecuteAsync(
                @"INSERT INTO users (id, tenant_id, email, name, role, status, created_at)
                  VALUES (@Id, @TenantId, 'export_user@test.com', 'Exporter User', 'user', 'active', @Now)",
                new { Id = userId, TenantId = tenantId, Now = now });

            await connection.ExecuteAsync(
                @"INSERT INTO feature_flags (id, tenant_id, flag_key, enabled, rollout_percentage, created_at)
                  VALUES (@Id, @TenantId, 'export-flag', 1, 100, @Now)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, Now = now });

            await connection.ExecuteAsync(
                @"INSERT INTO audit_events (id, tenant_id, action, resource_type, ts)
                  VALUES (@Id, @TenantId, 'TestExportAudit', 'None', @Now)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, Now = now });
        }

        // 2. Resolve job from DI container and execute synchronously
        using var scope = _factory.Services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IBackgroundTaskHandler>();
        var exportJob = handlers.First(h => h.JobType == "ExportTenantData");

        var payload = new ExportTenantDataPayload
        {
            TenantId = tenantId,
            ExportEventId = exportEventId
        };
        var payloadStr = JsonSerializer.Serialize(payload, AppJsonSerializerContext.Default.ExportTenantDataPayload);

                // Stub event publishing to capture ZIP path
        string? zipPath = null;
        _mockEventBus.PublishAsync(
            Arg.Do<TenantExportCompletedEvent>(e => zipPath = e.ZipPath),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await exportJob.ExecuteAsync(payloadStr, CancellationToken.None);

        // 3. Verify ZIP packaging and content
        await _mockEventBus.Received(1).PublishAsync(
            Arg.Any<TenantExportCompletedEvent>(),
            Arg.Any<CancellationToken>());

        Assert.NotNull(zipPath);
        Assert.True(File.Exists(zipPath));

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            Assert.NotNull(archive.GetEntry("tenant.json"));
            Assert.NotNull(archive.GetEntry("plan.json"));
            Assert.NotNull(archive.GetEntry("users.json"));
            Assert.NotNull(archive.GetEntry("feature_flags.json"));
            Assert.NotNull(archive.GetEntry("audit_events.json"));

            using var reader = new StreamReader(archive.GetEntry("tenant.json")!.Open());
            var tenantJson = reader.ReadToEnd();
            Assert.Contains("Export Tenant", tenantJson);
        }

        // Clean up ZIP file
        File.Delete(zipPath);

        // 4. Verify TenantExportCompleted audit event recorded
        using (var connection = await _dbFactory.CreateAsync())
        {
            var exportAudit = await connection.QuerySingleAsync<dynamic>(
                "SELECT action, resource_type, resource_id FROM audit_events WHERE id = @Id",
                new { Id = exportEventId });
                        Assert.Equal("TenantExportCompleted", (string)exportAudit.action);
            Assert.Equal("Tenant", (string)exportAudit.resource_type);
            Assert.Equal(tenantId, Guid.Parse((string)exportAudit.resource_id));
        }
    }
}
