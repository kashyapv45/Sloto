namespace SaasEngine.Domain.Shared;

/// <summary>
/// Abstraction for secret storage (Azure Key Vault, AWS Secrets Manager, etc.).
/// </summary>
public interface ISecretStore
{
    /// <summary>Retrieves a secret value by its key.</summary>
    /// <param name="key">The secret key or reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The secret value.</returns>
    Task<string> GetSecretAsync(string key, CancellationToken cancellationToken = default);
}
