using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SaasEngine.Domain.Shared;
using StackExchange.Redis;

namespace SaasEngine.Api.Infrastructure.Locks;

/// <summary>
/// Redis-backed implementation of IDistributedLock with a robust in-memory fallback.
/// </summary>
public sealed class RedisDistributedLock : IDistributedLock
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisDistributedLock> _logger;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemoryLocks = new();

    /// <summary>
    /// Initializes a new instance of RedisDistributedLock.
    /// </summary>
    public RedisDistributedLock(ILogger<RedisDistributedLock> logger, IConnectionMultiplexer? redis = null)
    {
        _logger = logger;
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task<IDisposable?> AcquireAsync(string key, TimeSpan expiry, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_redis is not null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var lockKey = $"lock:{key}";
                var token = Guid.NewGuid().ToString();

                var startTime = DateTime.UtcNow;
                do
                {
                    // Set NX (not exists) PX (milliseconds expiry)
                    var acquired = await db.StringSetAsync(lockKey, token, expiry, When.NotExists).ConfigureAwait(false);
                    if (acquired)
                    {
                        _logger.LogDebug("Acquired Redis lock for key: {Key}", key);
                        return new RedisLockHandle(db, lockKey, token, _logger);
                    }

                    var elapsed = DateTime.UtcNow - startTime;
                    if (elapsed >= timeout)
                    {
                        break;
                    }

                    // Exponential backoff or jittered sleep
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);

                } while (!cancellationToken.IsCancellationRequested);

                _logger.LogWarning("Failed to acquire Redis lock for key: {Key} within timeout.", key);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis lock acquisition failed for key: {Key}. Falling back to in-memory lock.", key);
            }
        }

        // Fallback to in-memory lock
        return await AcquireInMemoryLockAsync(key, timeout, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IDisposable?> AcquireInMemoryLockAsync(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var semaphore = InMemoryLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        var acquired = await semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            _logger.LogWarning("Failed to acquire in-memory lock for key: {Key} within timeout.", key);
            return null;
        }

        _logger.LogDebug("Acquired in-memory lock for key: {Key}", key);
        return new InMemoryLockHandle(semaphore, key, _logger);
    }

    private sealed class RedisLockHandle : IDisposable
    {
        private readonly IDatabase _db;
        private readonly string _lockKey;
        private readonly string _token;
        private readonly ILogger _logger;
        private int _disposed;

        public RedisLockHandle(IDatabase db, string lockKey, string token, ILogger logger)
        {
            _db = db;
            _lockKey = lockKey;
            _token = token;
            _logger = logger;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            try
            {
                // Release lock only if the token matches (using Lua script to avoid race conditions)
                var releaseScript = @"
                    if redis.call('get', KEYS[1]) == ARGV[1] then
                        return redis.call('del', KEYS[1])
                    else
                        return 0
                    end";

                _db.ScriptEvaluate(releaseScript, new RedisKey[] { _lockKey }, new RedisValue[] { _token });
                _logger.LogDebug("Released Redis lock for key: {Key}", _lockKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing Redis lock for key: {Key}", _lockKey);
            }
        }
    }

    private sealed class InMemoryLockHandle : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly string _key;
        private readonly ILogger _logger;
        private int _disposed;

        public InMemoryLockHandle(SemaphoreSlim semaphore, string key, ILogger logger)
        {
            _semaphore = semaphore;
            _key = key;
            _logger = logger;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            _semaphore.Release();
            _logger.LogDebug("Released in-memory lock for key: {Key}", _key);
        }
    }
}
