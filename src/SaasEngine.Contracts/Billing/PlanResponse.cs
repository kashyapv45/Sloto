namespace SaasEngine.Contracts.Billing;

/// <summary>Response containing plan details.</summary>
public sealed record PlanResponse
{
    /// <summary>Gets the plan identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the plan key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the maximum seats.</summary>
    public required int MaxSeats { get; init; }

    /// <summary>Gets the max API calls per minute.</summary>
    public required int MaxApiCallsPerMinute { get; init; }

    /// <summary>Gets the enabled features.</summary>
    public required IReadOnlyList<string> Features { get; init; }

    /// <summary>Gets the monthly price in cents.</summary>
    public required int PriceMonthlyCents { get; init; }
}
