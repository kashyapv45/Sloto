using System;
using System.Threading;
using System.Threading.Tasks;

namespace SaasEngine.Domain.Shared;

/// <summary>
/// Defines a contract for distributed locking.
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Acquires a lock for the specified key.
    /// </summary>
    /// <param name="key">The unique key representing the resource to lock.</param>
    /// <param name="expiry">The duration after which the lock will automatically expire.</param>
    /// <param name="timeout">The maximum time to block waiting to acquire the lock.</param>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    /// <returns>A disposable handle that releases the lock when disposed, or null if the lock could not be acquired within the timeout.</returns>
    Task<IDisposable?> AcquireAsync(string key, TimeSpan expiry, TimeSpan timeout, CancellationToken cancellationToken = default);
}
