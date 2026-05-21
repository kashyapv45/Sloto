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
using SaasEngine.Contracts.Tenancy;
using SaasEngine.Domain.Shared;
using Xunit;

namespace SaasEngine.IntegrationTests;

public sealed class TenantIntegrationTests : IClassFixture<WebApplicationFactory<SaasEngine.Api.Program>>
{
    private readonly WebApplicationFactory<SaasEngine.Api.Program> _factory;
    private readonly MockDbConnectionFactory _dbFactory;

    public TenantIntegrationTests(WebApplicationFactory<SaasEngine.Api.Program> factory)
    {
        _dbFactory = new MockDbConnectionFactory();
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

                // Register SQLite Mock Connection Factory
                services.AddSingleton<IAdminConnectionFactory>(_dbFactory);
                services.AddSingleton<IDbConnectionFactory>(_dbFactory);

                // Register Test Authentication Handler
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "TestScheme";
                    options.DefaultChallengeScheme = "TestScheme";
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", options => { });
            });
        });
    }

    [Fact]
    public async Task GetTenant_ReturnsNotFound_WhenTenantDoesNotExist()
    {
        // Arrange
        var client = _factory.CreateClient();
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/admin/tenants/{nonExistentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenant_CreatesTenantAndReturnsCreated()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new CreateTenantRequest
        {
            Name = "Integration Test Tenant",
            Tier = "standard",
            PlanKey = "starter",
            ConnectionSecretRef = null
        };

        // Act
        var response = await client.PostAsJsonAsync("/admin/tenants", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tenant = await response.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(tenant);
        Assert.Equal("Integration Test Tenant", tenant.Name);
        Assert.Equal("standard", tenant.Tier);
        Assert.Equal("active", tenant.Status);

        // Verify it can be retrieved
        var getResponse = await client.GetAsync($"/admin/tenants/{tenant.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getTenant = await getResponse.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(getTenant);
        Assert.Equal(tenant.Id, getTenant.Id);
    }

    [Fact]
    public async Task Middleware_AllowsRequest_WhenTenantIsActive()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        
        // Seed the tenant as active in the database
        using (var connection = await _dbFactory.CreateAsync())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO tenants (id, name, tier, plan_id, status, created_at)
                  VALUES (@Id, 'Active Tenant', 'standard', @PlanId, 'active', @Now)",
                new { Id = tenantId, PlanId = planId, Now = DateTimeOffset.UtcNow });
        }

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/tenants/" + tenantId);
        request.Headers.Add("X-Test-TenantId", tenantId.ToString());
        request.Headers.Add("X-Test-TenantTier", "standard");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Middleware_ReturnsForbidden_WhenTenantIsSuspended()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        
        // Seed the tenant as suspended in the database
        using (var connection = await _dbFactory.CreateAsync())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO tenants (id, name, tier, plan_id, status, created_at)
                  VALUES (@Id, 'Suspended Tenant', 'standard', @PlanId, 'suspended', @Now)",
                new { Id = tenantId, PlanId = planId, Now = DateTimeOffset.UtcNow });
        }

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/tenants/" + tenantId);
        request.Headers.Add("X-Test-TenantId", tenantId.ToString());
        request.Headers.Add("X-Test-TenantTier", "standard");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Inner class for Test Authentication Handler
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
            // If request has X-Test-TenantId, authenticate the user with that claim
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

            // Fallback for requests that don't need tenant validation / aren't setting headers
            var defaultClaims = new[] { new Claim(ClaimTypes.Name, "TestUser") };
            var defaultIdentity = new ClaimsIdentity(defaultClaims, "TestScheme");
            var defaultPrincipal = new ClaimsPrincipal(defaultIdentity);
            var defaultTicket = new AuthenticationTicket(defaultPrincipal, "TestScheme");

            return Task.FromResult(AuthenticateResult.Success(defaultTicket));
        }
    }
}
