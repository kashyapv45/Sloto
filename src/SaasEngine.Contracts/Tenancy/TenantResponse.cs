namespace SaasEngine.Contracts.Tenancy;

/// <summary>Response containing tenant details.</summary>
public sealed record TenantResponse
{
    /// <summary>Gets the tenant identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the isolation tier.</summary>
    public required string Tier { get; init; }

    /// <summary>Gets the plan identifier.</summary>
    public required Guid PlanId { get; init; }

    /// <summary>Gets the tenant status.</summary>
    public required string Status { get; init; }

    /// <summary>Gets the creation timestamp.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets the last update timestamp.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
