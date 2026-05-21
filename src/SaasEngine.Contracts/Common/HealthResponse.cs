namespace SaasEngine.Contracts.Common;

/// <summary>Health check response.</summary>
public sealed record HealthResponse
{
    /// <summary>Gets the health status.</summary>
    public required string Status { get; init; }
}
