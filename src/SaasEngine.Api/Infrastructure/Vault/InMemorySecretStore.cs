using System.Collections.Concurrent;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Infrastructure.Vault;

/// <summary>
/// In-memory secret store for local development.
/// NOT for production use — secrets are stored in plaintext in memory.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new();

    /// <summary>Seeds a secret for local development.</summary>
    /// <param name="key">The secret key.</param>
    /// <param name="value">The secret value.</param>
    public void SetSecret(string key, string value)
    {
        _secrets[key] = value;
    }

    /// <inheritdoc />
    public Task<string> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_secrets.TryGetValue(key, out var value))
        {
            return Task.FromResult(value);
        }

        throw new KeyNotFoundException($"Secret '{key}' was not found in the in-memory store.");
    }
}
