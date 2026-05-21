namespace SaasEngine.Domain.Billing;

/// <summary>
/// Represents a tenant-specific feature flag.
/// </summary>
public sealed record FeatureFlag
{
    /// <summary>Gets the unique identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant identifier.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the feature flag key.</summary>
    public required string FlagKey { get; init; }

    /// <summary>Gets whether the flag is enabled.</summary>
    public required bool Enabled { get; init; }

    /// <summary>Gets the rollout percentage (0-100).</summary>
    public required int RolloutPercentage { get; init; }

    /// <summary>Gets the creation timestamp.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
