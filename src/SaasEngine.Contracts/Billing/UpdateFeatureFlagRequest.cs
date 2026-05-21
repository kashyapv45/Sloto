namespace SaasEngine.Contracts.Billing;

/// <summary>Request to update a feature flag.</summary>
public sealed record UpdateFeatureFlagRequest
{
    /// <summary>Gets whether the flag should be enabled.</summary>
    public required bool Enabled { get; init; }

    /// <summary>Gets the rollout percentage (0-100).</summary>
    public int RolloutPercentage { get; init; } = 100;
}
