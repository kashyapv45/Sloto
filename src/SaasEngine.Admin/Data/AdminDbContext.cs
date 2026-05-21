using Microsoft.EntityFrameworkCore;

namespace SaasEngine.Admin.Data;

/// <summary>
/// EF Core DbContext for admin operations and migrations.
/// This context is NOT used in the AOT API project.
/// </summary>
public sealed class AdminDbContext : DbContext
{
    /// <summary>Initializes a new instance.</summary>
    public AdminDbContext(DbContextOptions<AdminDbContext> options) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tenants table
        modelBuilder.Entity<TenantEntity>(entity =>
        {
            entity.ToTable("tenants", t =>
            {
                t.HasCheckConstraint("ck_tenants_tier", "tier IN ('standard', 'professional', 'enterprise')");
                t.HasCheckConstraint("ck_tenants_status", "status IN ('active', 'suspended', 'deleted')");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Tier).HasColumnName("tier").HasMaxLength(20).IsRequired();
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("active");
            entity.Property(e => e.ConnectionSecretRef).HasColumnName("connection_secret_ref").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        // Users table
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Role).HasColumnName("role").HasMaxLength(50).IsRequired();
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").HasMaxLength(500).IsRequired();
            entity.Property(e => e.MfaSecret).HasColumnName("mfa_secret").HasMaxLength(500);
            entity.Property(e => e.MfaEnabled).HasColumnName("mfa_enabled").HasDefaultValue(false);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("active");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();
            entity.HasIndex(e => e.TenantId).HasFilter("status = 'active'");
        });

        // Plans table
        modelBuilder.Entity<PlanEntity>(entity =>
        {
            entity.ToTable("plans");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key").HasMaxLength(50).IsRequired();
            entity.Property(e => e.MaxSeats).HasColumnName("max_seats").IsRequired();
            entity.Property(e => e.MaxApiCallsPerMinute).HasColumnName("max_api_calls_per_min").IsRequired();
            entity.Property(e => e.Features).HasColumnName("features").HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.PriceMonthlyCents).HasColumnName("price_monthly_cents").IsRequired();

            entity.HasIndex(e => e.Key).IsUnique();
        });

        // Feature flags table
        modelBuilder.Entity<FeatureFlagEntity>(entity =>
        {
            entity.ToTable("feature_flags");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.FlagKey).HasColumnName("flag_key").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(false);
            entity.Property(e => e.RolloutPercentage).HasColumnName("rollout_percentage").HasDefaultValue(100);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(e => new { e.TenantId, e.FlagKey }).IsUnique();
        });

        // Audit events table (INSERT-only)
        modelBuilder.Entity<AuditEventEntity>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.ActorId).HasColumnName("actor_id");
            entity.Property(e => e.ActorEmail).HasColumnName("actor_email").HasMaxLength(200);
            entity.Property(e => e.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
            entity.Property(e => e.ResourceType).HasColumnName("resource_type").HasMaxLength(100);
            entity.Property(e => e.ResourceId).HasColumnName("resource_id");
            entity.Property(e => e.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsFixedLength();
            entity.Property(e => e.BeforeState).HasColumnName("before_state").HasColumnType("jsonb");
            entity.Property(e => e.AfterState).HasColumnName("after_state").HasColumnType("jsonb");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address");
            entity.Property(e => e.UserAgent).HasColumnName("user_agent");
            entity.Property(e => e.Timestamp).HasColumnName("ts").HasDefaultValueSql("now()").IsRequired();
        });

        // Outbox events table
        modelBuilder.Entity<OutboxEventEntity>(entity =>
        {
            entity.ToTable("outbox_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.AggregateId).HasColumnName("aggregate_id");
            entity.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("pending");
            entity.Property(e => e.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        });

        // Scheduled jobs table
        modelBuilder.Entity<ScheduledJobEntity>(entity =>
        {
            entity.ToTable("scheduled_jobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(200).IsRequired();
            entity.Property(e => e.CronExpression).HasColumnName("cron").HasMaxLength(100).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
            entity.Property(e => e.LastRunAt).HasColumnName("last_run");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(e => new { e.TenantId, e.JobKey }).IsUnique();
        });
    }
}
