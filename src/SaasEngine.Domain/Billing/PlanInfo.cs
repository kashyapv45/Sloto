namespace SaasEngine.Domain.Billing;

/// <summary>
/// Lightweight plan information record used by ITenantService.
/// </summary>
public sealed record PlanInfo
{
    /// <summary>Gets the plan key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the maximum seats.</summary>
    public required int MaxSeats { get; init; }

    /// <summary>Gets the maximum API calls per minute.</summary>
    public required int MaxApiCallsPerMinute { get; init; }

    /// <summary>Gets the enabled features.</summary>
    public required IReadOnlyList<string> Features { get; init; }
}
