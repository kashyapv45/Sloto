namespace SaasEngine.Domain.Tenancy;

/// <summary>
/// Represents a tenant in the multi-tenant SaaS platform.
/// Immutable domain entity.
/// </summary>
public sealed record Tenant
{
    /// <summary>Gets the unique tenant identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the isolation tier for this tenant.</summary>
    public required TenantTier Tier { get; init; }

    /// <summary>Gets the plan identifier assigned to this tenant.</summary>
    public required Guid PlanId { get; init; }

    /// <summary>Gets the current status of this tenant.</summary>
    public required TenantStatus Status { get; init; }

    /// <summary>Gets the reference to the encrypted connection string secret (Enterprise tier only).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ConnectionSecretRef { get; init; }

    /// <summary>Gets the UTC timestamp when this tenant was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets the UTC timestamp of the last update.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
