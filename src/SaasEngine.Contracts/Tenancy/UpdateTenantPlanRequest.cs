namespace SaasEngine.Contracts.Tenancy;

/// <summary>Request to change a tenant's plan.</summary>
public sealed record UpdateTenantPlanRequest
{
    /// <summary>Gets the new plan key.</summary>
    public required string PlanKey { get; init; }
}
