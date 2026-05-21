namespace SaasEngine.Domain.Billing;

/// <summary>
/// Represents a subscription plan with feature limits.
/// Immutable domain entity.
/// </summary>
public sealed record Plan
{
    /// <summary>Gets the unique plan identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the plan key (e.g., 'starter', 'growth', 'enterprise').</summary>
    public required string Key { get; init; }

    /// <summary>Gets the maximum number of seats allowed.</summary>
    public required int MaxSeats { get; init; }

    /// <summary>Gets the maximum API calls per minute.</summary>
    public required int MaxApiCallsPerMinute { get; init; }

    /// <summary>Gets the list of enabled feature keys.</summary>
    public required IReadOnlyList<string> Features { get; init; }

    /// <summary>Gets the monthly price in cents.</summary>
    public required int PriceMonthlyCents { get; init; }
}
