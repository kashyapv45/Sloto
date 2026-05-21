namespace SaasEngine.Admin.Data;

/// <summary>EF Core entity for tenants table.</summary>
public sealed class TenantEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = "standard";
    public Guid PlanId { get; set; }
    public string Status { get; set; } = "active";
    public string? ConnectionSecretRef { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>EF Core entity for users table.</summary>
public sealed class UserEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "member";
    public string PasswordHash { get; set; } = string.Empty;
    public string? MfaSecret { get; set; }
    public bool MfaEnabled { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>EF Core entity for plans table.</summary>
public sealed class PlanEntity
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public int MaxSeats { get; set; }
    public int MaxApiCallsPerMinute { get; set; }
    public string Features { get; set; } = "[]";
    public int PriceMonthlyCents { get; set; }
}

/// <summary>EF Core entity for feature_flags table.</summary>
public sealed class FeatureFlagEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string FlagKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int RolloutPercentage { get; set; } = 100;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>EF Core entity for audit_events table.</summary>
public sealed class AuditEventEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ResourceType { get; set; }
    public Guid? ResourceId { get; set; }
    public string? PayloadHash { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>EF Core entity for outbox_events table.</summary>
public sealed class OutboxEventEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? AggregateId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

/// <summary>EF Core entity for scheduled_jobs table.</summary>
public sealed class ScheduledJobEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string JobKey { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
