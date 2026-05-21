using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SaasEngine.Contracts.Billing;
using SaasEngine.Contracts.Identity;
using SaasEngine.Contracts.Tenancy;
using SaasEngine.Domain.Shared;
using StackExchange.Redis;
using Xunit;

namespace SaasEngine.IntegrationTests;

public sealed class PlanEnforcementIntegrationTests : IClassFixture<WebApplicationFactory<SaasEngine.Api.Program>>
{
    private readonly WebApplicationFactory<SaasEngine.Api.Program> _factory;
    private readonly MockDbConnectionFactory _dbFactory;

    public PlanEnforcementIntegrationTests(WebApplicationFactory<SaasEngine.Api.Program> factory)
    {
        _dbFactory = new MockDbConnectionFactory();
        _factory = factory;
    }

    private HttpClient CreateClient(IConnectionMultiplexer? redisMock = null)
    {
        var customFactory = _factory.WithWebHostBuilder(builder =>
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

                // Register SQLite Mock Connection Factory
                services.AddSingleton<IAdminConnectionFactory>(_dbFactory);
                services.AddSingleton<IDbConnectionFactory>(_dbFactory);

                // Override Redis configuration
                var redisDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IConnectionMultiplexer));
                if (redisDescriptor != null) services.Remove(redisDescriptor);

                if (redisMock != null)
                {
                    services.AddSingleton<IConnectionMultiplexer>(redisMock);
                }
                else
                {
                    services.AddSingleton<IConnectionMultiplexer>(sp => null!);
                }

                // Register Test Authentication Handler
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "TestScheme";
                    options.DefaultChallengeScheme = "TestScheme";
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", options => { });
            });
        });

        return customFactory.CreateClient();
    }

    [Fact]
    public async Task SeatsLimitPolicy_IsEnforced_WhenRegisteringExceedsMaxSeats()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var planId = Guid.Parse("00000000-0000-0000-0000-000000000001"); // starter plan (max_seats = 5)

        using (var connection = await _dbFactory.CreateAsync())
        {
            // Seed tenant
            await connection.ExecuteAsync(
                @"INSERT INTO tenants (id, name, tier, plan_id, status, created_at)
                  VALUES (@Id, 'Seats Test Tenant', 'standard', @PlanId, 'active', @Now)",
                new { Id = tenantId, PlanId = planId, Now = DateTimeOffset.UtcNow });

            // Seed exactly 5 active users
            for (int i = 1; i <= 5; i++)
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO users (id, tenant_id, email, name, role, password_hash, mfa_enabled, status, created_at)
                      VALUES (@Id, @TenantId, @Email, @Name, 'member', 'hash', 0, 'active', @Now)",
                    new
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId.ToString(),
                        Email = $"user{i}@test.com",
                        Name = $"User {i}",
                        Now = DateTimeOffset.UtcNow
                    });
            }
        }

        var client = CreateClient(); // No Redis mock, DB fallback will be tested

        var registerRequest = new RegisterUserRequest
        {
            Email = "excess@test.com",
            Name = "Excess User",
            Password = "SecurePassword123!",
            Role = "member"
        };

        var requestMsg = new HttpRequestMessage(HttpMethod.Post, "/auth/register")
        {
            Content = JsonContent.Create(registerRequest)
        };
        requestMsg.Headers.Add("X-Tenant-Id", tenantId.ToString());

        // Act
        var response = await client.SendAsync(requestMsg);

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Plan Limit Exceeded", problem.Title);
        Assert.Contains("seats", problem.Detail);
    }

    [Fact]
    public async Task PlanUpgrade_ClearsCache_AndAllowsOperation_WhenExceededPreviously()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var starterPlanId = Guid.Parse("00000000-0000-0000-0000-000000000001"); // starter (max_seats = 5)

        using (var connection = await _dbFactory.CreateAsync())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO tenants (id, name, tier, plan_id, status, created_at)
                  VALUES (@Id, 'Upgrade Test Tenant', 'standard', @PlanId, 'active', @Now)",
                new { Id = tenantId, PlanId = starterPlanId, Now = DateTimeOffset.UtcNow });

            // Seed 5 active users
            for (int i = 1; i <= 5; i++)
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO users (id, tenant_id, email, name, role, password_hash, mfa_enabled, status, created_at)
                      VALUES (@Id, @TenantId, @Email, @Name, 'member', 'hash', 0, 'active', @Now)",
                    new
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId.ToString(),
                        Email = $"upgrade{i}@test.com",
                        Name = $"User {i}",
                        Now = DateTimeOffset.UtcNow
                    });
            }
        }

        // Mock Redis to assert that cache bust (KeyDeleteAsync) is invoked
        var redisMock = Substitute.For<IConnectionMultiplexer>();
        var dbMock = Substitute.For<IDatabase>();
        redisMock.IsConnected.Returns(true);
        redisMock.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(dbMock);

        var client = CreateClient(redisMock);

        // 1. Try to register 6th user -> should fail (Seats limit exceeded)
        var registerRequest = new RegisterUserRequest
        {
            Email = "excess2@test.com",
            Name = "Excess User 2",
            Password = "SecurePassword123!",
            Role = "member"
        };

        var requestMsg = new HttpRequestMessage(HttpMethod.Post, "/auth/register")
        {
            Content = JsonContent.Create(registerRequest)
        };
        requestMsg.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var initialResponse = await client.SendAsync(requestMsg);
        Assert.Equal(HttpStatusCode.TooManyRequests, initialResponse.StatusCode);

        // 2. Perform plan upgrade to growth (max_seats = 25)
        var planRequest = new UpdateTenantPlanRequest
        {
            PlanKey = "growth"
        };
        var upgradeResponse = await client.PatchAsJsonAsync($"/admin/tenants/{tenantId}/plan", planRequest);
        Assert.Equal(HttpStatusCode.NoContent, upgradeResponse.StatusCode);

        // Assert: Redis Plan changed event handler was triggered and cache was busted
        await dbMock.Received().KeyDeleteAsync(Arg.Is<RedisKey>(k => k.ToString() == $"saas:plan:{tenantId}"));

        // 3. Re-try user registration -> should now succeed!
        var retryMsg = new HttpRequestMessage(HttpMethod.Post, "/auth/register")
        {
            Content = JsonContent.Create(registerRequest)
        };
        retryMsg.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var retryResponse = await client.SendAsync(retryMsg);
        Assert.Equal(HttpStatusCode.Created, retryResponse.StatusCode);
    }

    [Fact]
    public async Task RateLimitPolicy_EnforcesSlidingWindow_AndReturns429TooManyRequests()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var starterPlanId = Guid.Parse("00000000-0000-0000-0000-000000000001"); // starter (max_api_calls_per_min = 60)

        using (var connection = await _dbFactory.CreateAsync())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO tenants (id, name, tier, plan_id, status, created_at)
                  VALUES (@Id, 'Rate Limit Tenant', 'standard', @PlanId, 'active', @Now)",
                new { Id = tenantId, PlanId = starterPlanId, Now = DateTimeOffset.UtcNow });
        }

        var redisMock = Substitute.For<IConnectionMultiplexer>();
        var dbMock = Substitute.For<IDatabase>();
        redisMock.IsConnected.Returns(true);
        redisMock.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(dbMock);

        // Mock rate limit Redis evaluation to return 61 (which is > limit of 60)
        dbMock.ScriptEvaluateAsync(Arg.Any<LuaScript>(), Arg.Any<object>(), Arg.Any<StackExchange.Redis.CommandFlags>())
            .Returns(StackExchange.Redis.RedisResult.Create(61));

        var client = CreateClient(redisMock);

        var requestMsg = new HttpRequestMessage(HttpMethod.Get, "/features/advanced-analytics");
        requestMsg.Headers.Add("X-Test-TenantId", tenantId.ToString());
        requestMsg.Headers.Add("X-Test-TenantTier", "standard");

        // Act
        var response = await client.SendAsync(requestMsg);

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));

        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Plan Limit Exceeded", problem.Title);
        Assert.NotNull(problem.Detail);
        Assert.Contains("api_rate", problem.Detail!.ToLowerInvariant());
    }

    [Fact]
    public async Task FeatureFlags_CanBeManagedByAdmin_AndEvaluatedByClient()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var starterPlanId = Guid.Parse("00000000-0000-0000-0000-000000000001"); // starter

        using (var connection = await _dbFactory.CreateAsync())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO tenants (id, name, tier, plan_id, status, created_at)
                  VALUES (@Id, 'Feature Flags Tenant', 'standard', @PlanId, 'active', @Now)",
                new { Id = tenantId, PlanId = starterPlanId, Now = DateTimeOffset.UtcNow });
        }

        var redisMock = Substitute.For<IConnectionMultiplexer>();
        var dbMock = Substitute.For<IDatabase>();
        redisMock.IsConnected.Returns(true);
        redisMock.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(dbMock);

        // Allow rate limit checks to pass (return count=1, well under the 60 limit)
        dbMock.ScriptEvaluateAsync(Arg.Any<LuaScript>(), Arg.Any<object>(), Arg.Any<StackExchange.Redis.CommandFlags>())
            .Returns(StackExchange.Redis.RedisResult.Create(1));

        var client = CreateClient(redisMock);

        // 1. Check feature "advanced-analytics" before override -> should be false (starter plan doesn't have it)
        var checkMsgBefore = new HttpRequestMessage(HttpMethod.Get, "/features/advanced-analytics");
        checkMsgBefore.Headers.Add("X-Test-TenantId", tenantId.ToString());
        var checkBeforeResponse = await client.SendAsync(checkMsgBefore);
        Assert.Equal(HttpStatusCode.OK, checkBeforeResponse.StatusCode);
        var flagResponseBefore = await checkBeforeResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        Assert.NotNull(flagResponseBefore);
        Assert.False(flagResponseBefore.Enabled);

        // 2. Put admin override: enable "advanced-analytics" for this tenant
        var updateRequest = new UpdateFeatureFlagRequest
        {
            Enabled = true,
            RolloutPercentage = 100
        };
        var overrideResponse = await client.PutAsJsonAsync($"/admin/tenants/{tenantId}/flags/advanced-analytics", updateRequest);
        Assert.Equal(HttpStatusCode.NoContent, overrideResponse.StatusCode);

        // Assert: PUT override busted the feature cache in Redis
        await dbMock.Received().KeyDeleteAsync(Arg.Is<RedisKey>(k => k.ToString() == $"saas:feature:{tenantId}:advanced-analytics"));

        // 3. Retrieve tenant overrides list via admin API
        var getOverridesResponse = await client.GetAsync($"/admin/tenants/{tenantId}/flags");
        Assert.Equal(HttpStatusCode.OK, getOverridesResponse.StatusCode);
        var overridesList = await getOverridesResponse.Content.ReadFromJsonAsync<List<FeatureFlagResponse>>();
        Assert.NotNull(overridesList);
        var overrideItem = Assert.Single(overridesList!);
        Assert.Equal("advanced-analytics", overrideItem.FlagKey);
        Assert.True(overrideItem.Enabled);

        // 4. Check feature status again -> should now return true!
        var checkMsgAfter = new HttpRequestMessage(HttpMethod.Get, "/features/advanced-analytics");
        checkMsgAfter.Headers.Add("X-Test-TenantId", tenantId.ToString());
        var checkAfterResponse = await client.SendAsync(checkMsgAfter);
        Assert.Equal(HttpStatusCode.OK, checkAfterResponse.StatusCode);
        var flagResponseAfter = await checkAfterResponse.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        Assert.NotNull(flagResponseAfter);
        Assert.True(flagResponseAfter.Enabled);
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.TryGetValue("X-Test-TenantId", out var tenantIdValues))
            {
                var tenantId = tenantIdValues.ToString();
                var tier = Request.Headers.TryGetValue("X-Test-TenantTier", out var tierValues)
                    ? tierValues.ToString()
                    : "standard";

                var claims = new[]
                {
                    new Claim(ClaimTypes.Name, "TestUser"),
                    new Claim("tid", tenantId),
                    new Claim("tier", tier)
                };

                var identity = new ClaimsIdentity(claims, "TestScheme");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "TestScheme");

                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            var defaultClaims = new[] { new Claim(ClaimTypes.Name, "TestUser") };
            var defaultIdentity = new ClaimsIdentity(defaultClaims, "TestScheme");
            var defaultPrincipal = new ClaimsPrincipal(defaultIdentity);
            var defaultTicket = new AuthenticationTicket(defaultPrincipal, "TestScheme");

            return Task.FromResult(AuthenticateResult.Success(defaultTicket));
        }
    }
}
