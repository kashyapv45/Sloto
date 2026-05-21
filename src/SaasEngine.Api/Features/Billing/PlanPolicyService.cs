using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using SaasEngine.Domain.Billing;
using SaasEngine.Domain.Shared;
using StackExchange.Redis;

namespace SaasEngine.Api.Features.Billing;

/// <summary>
/// Enforces subscription plan policies and feature flags for tenants.
/// Backed by Redis with SQL database fallback.
/// </summary>
public sealed class PlanPolicyService : IPlanPolicyService
{
    private readonly IAdminConnectionFactory _adminDb;
    private readonly IDbConnectionFactory _tenantDb;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<PlanPolicyService> _logger;

    private static readonly LuaScript RateLimitScript = LuaScript.Prepare(@"
        local key = KEYS[1]
        local limit = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        local current = redis.call('get', key)
        if current and tonumber(current) >= limit then
            return tonumber(current)
        end
        local newVal = redis.call('incr', key)
        if newVal == 1 then
            redis.call('expire', key, window)
        end
        return newVal
    ");

    /// <summary>
    /// Initializes a new instance of <see cref="PlanPolicyService"/>.
    /// </summary>
    public PlanPolicyService(
        IAdminConnectionFactory adminDb,
        IDbConnectionFactory tenantDb,
        IConnectionMultiplexer? redis,
        ILogger<PlanPolicyService> logger)
    {
        _adminDb = adminDb;
        _tenantDb = tenantDb;
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnforceSeatsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var plan = await GetPlanInfoAsync(tenantId, cancellationToken).ConfigureAwait(false);

        // Count active users in the tenant database
        using var connection = await _tenantDb.CreateAsync(cancellationToken).ConfigureAwait(false);
        var activeUsersCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM users WHERE tenant_id = @TenantId AND status = 'active'",
            new { TenantId = tenantId.ToString() }).ConfigureAwait(false);

        if (activeUsersCount >= plan.MaxSeats)
        {
            _logger.LogWarning("Seat limit exceeded for tenant {TenantId}. Active seats: {Active}, Max allowed: {Max}",
                tenantId, activeUsersCount, plan.MaxSeats);
            throw new PlanLimitExceededException(tenantId, "seats");
        }
    }

    /// <inheritdoc />
    public async Task EnforceApiRateAsync(Guid tenantId, string endpoint, CancellationToken cancellationToken = default)
    {
        var plan = await GetPlanInfoAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var limit = plan.MaxApiCallsPerMinute;

        if (_redis is null || !_redis.IsConnected)
        {
            _logger.LogWarning("Redis is unavailable. Bypassing API rate limit check for tenant {TenantId}.", tenantId);
            return;
        }

        var db = _redis.GetDatabase();
        var windowTimestamp = DateTimeOffset.UtcNow.Ticks / TimeSpan.FromSeconds(60).Ticks;
        var key = $"saas:ratelimit:{tenantId}:{endpoint}:{windowTimestamp}";

        try
        {
            var result = await db.ScriptEvaluateAsync(RateLimitScript, new { key = (RedisKey)key, limit, window = 60 }).ConfigureAwait(false);
            var currentCount = (long)result;

            if (currentCount > limit)
            {
                var retryAfter = 60 - DateTimeOffset.UtcNow.Second;
                _logger.LogWarning("API rate limit exceeded for tenant {TenantId}. Current: {Current}, Limit: {Limit}",
                    tenantId, currentCount, limit);
                throw new PlanLimitExceededException(tenantId, "api_rate", retryAfter);
            }
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to evaluate Redis rate limit for tenant {TenantId}. Degrading gracefully.", tenantId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsFeatureEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"saas:feature:{tenantId}:{featureKey}";

        if (_redis is not null && _redis.IsConnected)
        {
            try
            {
                var cached = await _redis.GetDatabase().StringGetAsync(cacheKey).ConfigureAwait(false);
                if (cached.HasValue)
                {
                    return (bool)cached;
                }
            }
            catch (RedisException ex)
            {
                _logger.LogError(ex, "Redis error reading feature flag cache for {TenantId}:{FeatureKey}", tenantId, featureKey);
            }
        }

        var isEnabled = await EvaluateFeatureFlagAsync(tenantId, featureKey, cancellationToken).ConfigureAwait(false);

        if (_redis is not null && _redis.IsConnected)
        {
            try
            {
                await _redis.GetDatabase().StringSetAsync(cacheKey, isEnabled, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            }
            catch (RedisException ex)
            {
                _logger.LogError(ex, "Redis error caching feature flag for {TenantId}:{FeatureKey}", tenantId, featureKey);
            }
        }

        return isEnabled;
    }

    private async Task<bool> EvaluateFeatureFlagAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken)
    {
        // 1. Check feature flag overrides in tenant database
        using var connection = await _tenantDb.CreateAsync(cancellationToken).ConfigureAwait(false);
        var flag = await connection.QuerySingleOrDefaultAsync<FeatureFlagRecord>(
            "SELECT enabled, rollout_percentage FROM feature_flags WHERE tenant_id = @TenantId AND flag_key = @FeatureKey",
            new { TenantId = tenantId.ToString(), FeatureKey = featureKey }).ConfigureAwait(false);

        if (flag is not null)
        {
            if (!flag.Enabled) return false;
            if (flag.Rollout_percentage >= 100) return true;

            // Deterministic rollout bucket (0-99)
            return GetRolloutBucket(tenantId) < flag.Rollout_percentage;
        }

        // 2. Fall back to subscription plan features
        var plan = await GetPlanInfoAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return plan.Features.Contains(featureKey, StringComparer.OrdinalIgnoreCase);
    }

    private static int GetRolloutBucket(Guid tenantId)
    {
        var bytes = tenantId.ToByteArray();
        uint hash = 0;
        foreach (var b in bytes)
        {
            hash = (hash * 31) + b;
        }
        return (int)(hash % 100);
    }

    private async Task<PlanInfo> GetPlanInfoAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var cacheKey = $"saas:plan:{tenantId}";

        if (_redis is not null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var cached = await db.HashGetAllAsync(cacheKey).ConfigureAwait(false);
                if (cached.Length > 0)
                {
                    var dict = cached.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());
                    return new PlanInfo
                    {
                        Key = dict["key"],
                        MaxSeats = int.Parse(dict["max_seats"]),
                        MaxApiCallsPerMinute = int.Parse(dict["max_api_calls_per_min"]),
                        Features = JsonSerializer.Deserialize(dict["features"], SaasEngine.Api.Infrastructure.Serialization.AppJsonSerializerContext.Default.ListString) ?? new List<string>()
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis error reading plan cache for tenant {TenantId}.", tenantId);
            }
        }

        // Database query path
        using var connection = await _adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);
        var record = await connection.QuerySingleOrDefaultAsync<PlanDbRecord>(
            @"SELECT p.key, p.max_seats, p.max_api_calls_per_min, p.features, p.price_monthly_cents
              FROM tenants t
              JOIN plans p ON t.plan_id = p.id
              WHERE t.id = @TenantId AND t.status = 'active'",
            new { TenantId = tenantId.ToString() }).ConfigureAwait(false);

        if (record is null)
        {
            _logger.LogWarning("Active tenant {TenantId} not found in admin DB. Defaulting to starter plan.", tenantId);
            return new PlanInfo
            {
                Key = "starter",
                MaxSeats = 5,
                MaxApiCallsPerMinute = 60,
                Features = Array.Empty<string>()
            };
        }

        var features = JsonSerializer.Deserialize(record.Features, SaasEngine.Api.Infrastructure.Serialization.AppJsonSerializerContext.Default.ListString) ?? new List<string>();

        var planInfo = new PlanInfo
        {
            Key = record.Key,
            MaxSeats = record.Max_seats,
            MaxApiCallsPerMinute = record.Max_api_calls_per_min,
            Features = features
        };

        // Cache the result in Redis
        if (_redis is not null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var hashEntries = new HashEntry[]
                {
                    new("key", planInfo.Key),
                    new("max_seats", planInfo.MaxSeats),
                    new("max_api_calls_per_min", planInfo.MaxApiCallsPerMinute),
                    new("features", record.Features)
                };
                await db.HashSetAsync(cacheKey, hashEntries).ConfigureAwait(false);
                await db.KeyExpireAsync(cacheKey, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            }
            catch (RedisException ex)
            {
                _logger.LogError(ex, "Redis error caching plan info for tenant {TenantId}.", tenantId);
            }
        }

        return planInfo;
    }

    private sealed record PlanDbRecord
    {
        public required string Key { get; init; }
        public required int Max_seats { get; init; }
        public required int Max_api_calls_per_min { get; init; }
        public required string Features { get; init; }
        public required int Price_monthly_cents { get; init; }
    }

    private sealed record FeatureFlagRecord
    {
        public required bool Enabled { get; init; }
        public required int Rollout_percentage { get; init; }
    }
}
