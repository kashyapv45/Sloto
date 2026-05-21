using StackExchange.Redis;

namespace SaasEngine.Api.Infrastructure.Redis;

/// <summary>
/// Provides Redis connection multiplexer access.
/// Registered as singleton in DI container.
/// </summary>
public sealed class RedisConnectionProvider : IDisposable
{
    private readonly Lazy<IConnectionMultiplexer> _multiplexer;
    private bool _disposed;

    /// <summary>Initializes a new instance of <see cref="RedisConnectionProvider"/>.</summary>
    /// <param name="connectionString">The Redis connection string.</param>
    public RedisConnectionProvider(string connectionString)
    {
        _multiplexer = new Lazy<IConnectionMultiplexer>(
            () => ConnectionMultiplexer.Connect(connectionString));
    }

    /// <summary>Gets the Redis connection multiplexer.</summary>
    /// <returns>The connection multiplexer.</returns>
    public IConnectionMultiplexer GetMultiplexer() => _multiplexer.Value;

    /// <summary>Gets a Redis database instance.</summary>
    /// <param name="db">The database number (default -1 for default database).</param>
    /// <returns>A Redis database instance.</returns>
    public IDatabase GetDatabase(int db = -1) => _multiplexer.Value.GetDatabase(db);

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed && _multiplexer.IsValueCreated)
        {
            _multiplexer.Value.Dispose();
            _disposed = true;
        }
    }
}
