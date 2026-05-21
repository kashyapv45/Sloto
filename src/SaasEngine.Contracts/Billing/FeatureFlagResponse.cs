namespace SaasEngine.Contracts.Billing;

/// <summary>Response containing feature flag state.</summary>
public sealed record FeatureFlagResponse
{
    /// <summary>Gets the flag key.</summary>
    public required string FlagKey { get; init; }

    /// <summary>Gets whether the flag is enabled.</summary>
    public required bool Enabled { get; init; }

    /// <summary>Gets the rollout percentage.</summary>
    public required int RolloutPercentage { get; init; }
}
