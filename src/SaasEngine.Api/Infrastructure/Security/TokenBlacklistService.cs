using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace SaasEngine.Api.Infrastructure.Security;

/// <summary>
/// Manages JWT token blacklisting using Redis with an in-memory fallback for local dev/testing.
/// </summary>
public sealed class TokenBlacklistService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<TokenBlacklistService> _logger;
    private static readonly ConcurrentDictionary<string, DateTimeOffset> InMemoryFallback = new();

    /// <summary>
    /// Initializes a new instance of TokenBlacklistService.
    /// </summary>
    public TokenBlacklistService(ILogger<TokenBlacklistService> logger, IConnectionMultiplexer? redis = null)
    {
        _logger = logger;
        _redis = redis;
    }

    /// <summary>
    /// Blacklists the specified JWT token ID (jti) for the duration of its remaining lifetime.
    /// </summary>
    public async Task BlacklistTokenAsync(string jti, TimeSpan expiration)
    {
        if (_redis is not null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                await db.StringSetAsync($"blacklist:{jti}", "true", expiration).ConfigureAwait(false);
                _logger.LogInformation("Successfully blacklisted token JTI: {Jti} in Redis.", jti);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write token {Jti} to Redis blacklist, falling back to in-memory cache.", jti);
            }
        }

        // Fallback to in-memory
        InMemoryFallback[jti] = DateTimeOffset.UtcNow.Add(expiration);

        // Run fire-and-forget cleanup
        _ = Task.Run(CleanInMemoryFallback);
    }

    /// <summary>
    /// Checks whether the specified JWT token ID (jti) is currently blacklisted.
    /// </summary>
    public async Task<bool> IsBlacklistedAsync(string jti)
    {
        if (string.IsNullOrWhiteSpace(jti)) return false;

        if (_redis is not null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var isBlacklisted = await db.KeyExistsAsync($"blacklist:{jti}").ConfigureAwait(false);
                if (isBlacklisted) return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query Redis blacklist for JTI: {Jti}, checking in-memory fallback.", jti);
            }
        }

        if (InMemoryFallback.TryGetValue(jti, out var expiry))
        {
            if (expiry > DateTimeOffset.UtcNow)
            {
                return true;
            }
            InMemoryFallback.TryRemove(jti, out _);
        }

        return false;
    }

    private static void CleanInMemoryFallback()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in InMemoryFallback)
        {
            if (kvp.Value <= now)
            {
                InMemoryFallback.TryRemove(kvp.Key, out _);
            }
        }
    }
}
