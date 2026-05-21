namespace SaasEngine.Contracts.Tenancy;

/// <summary>Request to create a new tenant.</summary>
public sealed record CreateTenantRequest
{
    /// <summary>Gets the tenant display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the isolation tier (standard, professional, enterprise).</summary>
    public required string Tier { get; init; }

    /// <summary>Gets the plan key to assign.</summary>
    public required string PlanKey { get; init; }

    /// <summary>Gets the connection secret reference (enterprise tier only).</summary>
    public string? ConnectionSecretRef { get; init; }
}
