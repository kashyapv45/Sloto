namespace SaasEngine.Contracts.Tenancy;

/// <summary>Request to update a tenant's status.</summary>
public sealed record UpdateTenantStatusRequest
{
    /// <summary>Gets the new status (active, suspended).</summary>
    public required string Status { get; init; }
}
