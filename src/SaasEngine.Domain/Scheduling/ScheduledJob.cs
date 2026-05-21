namespace SaasEngine.Domain.Scheduling;

/// <summary>
/// Represents a tenant-scoped scheduled job.
/// </summary>
public sealed record ScheduledJob
{
    /// <summary>Gets the unique job identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant identifier.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the unique job key.</summary>
    public required string JobKey { get; init; }

    /// <summary>Gets the cron expression for scheduling.</summary>
    public required string CronExpression { get; init; }

    /// <summary>Gets the idempotency key for this job.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Gets the timestamp of the last execution.</summary>
    public DateTimeOffset? LastRunAt { get; init; }

    /// <summary>Gets the creation timestamp.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
