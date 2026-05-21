namespace SaasEngine.Contracts.Scheduling;

/// <summary>Request to create a custom scheduled job (enterprise only).</summary>
public sealed record CreateJobRequest
{
    /// <summary>Gets the unique job key.</summary>
    public required string JobKey { get; init; }

    /// <summary>Gets the cron expression.</summary>
    public required string CronExpression { get; init; }
}
