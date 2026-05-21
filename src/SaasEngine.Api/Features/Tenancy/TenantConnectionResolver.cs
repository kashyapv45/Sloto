using System.Collections.Concurrent;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Features.Tenancy;

/// <summary>
/// Resolves database connections based on tenant isolation tier.
/// Standard/Professional: shared connection string from config.
/// Enterprise: per-tenant encrypted connection from ISecretStore with 15-min cache.
/// </summary>
public sealed class TenantConnectionResolver : ITenantConnectionResolver
{
    private readonly IConfiguration _configuration;
    private readonly ISecretStore _secretStore;
    private readonly ConcurrentDictionary<Guid, (string ConnectionString, DateTimeOffset ExpiresAt)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    /// <summary>Initializes a new instance.</summary>
    public TenantConnectionResolver(IConfiguration configuration, ISecretStore secretStore)
    {
        _configuration = configuration;
        _secretStore = secretStore;
    }

    /// <inheritdoc />
    public string Resolve(Guid tenantId, string tier)
    {
        if (!string.Equals(tier, "enterprise", StringComparison.OrdinalIgnoreCase))
        {
            return _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection string is not configured.");
        }

        // Enterprise: check cache, then resolve from secret store
        if (_cache.TryGetValue(tenantId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.ConnectionString;
        }

        // Synchronous resolution (called from property getter) — cache miss
        var connectionString = _secretStore.GetSecretAsync($"tenant-{tenantId}-connection").GetAwaiter().GetResult();
        _cache[tenantId] = (connectionString, DateTimeOffset.UtcNow.Add(CacheTtl));
        return connectionString;
    }
}
