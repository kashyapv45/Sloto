namespace SaasEngine.Contracts.Scheduling;

/// <summary>Response containing job details.</summary>
public sealed record JobResponse
{
    /// <summary>Gets the job identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the job key.</summary>
    public required string JobKey { get; init; }

    /// <summary>Gets the cron expression.</summary>
    public required string CronExpression { get; init; }

    /// <summary>Gets the last run timestamp.</summary>
    public DateTimeOffset? LastRunAt { get; init; }

    /// <summary>Gets the creation timestamp.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
