using Dapper;
using MediatR;
using SaasEngine.Contracts.Billing;
using SaasEngine.Domain.Billing;
using SaasEngine.Domain.Shared;
using StackExchange.Redis;

namespace SaasEngine.Api.Features.Billing;

// ─── GET /features/{key} ─────────────────────────────────────────

/// <summary>Query to evaluate if a feature flag is enabled for the current tenant.</summary>
public sealed record GetFeatureFlagStatusQuery(string FlagKey, Guid TenantId) : IRequest<FeatureFlagResponse>, ITenantAwareRequest, IPlanEnforcedRequest
{
    /// <inheritdoc />
    public IReadOnlyList<PlanEnforcementPolicy> PlanPolicies { get; } =
        [new PlanEnforcementPolicy(PlanPolicyType.ApiRate)];
}

/// <summary>Handler for GetFeatureFlagStatusQuery.</summary>
public sealed class GetFeatureFlagStatusQueryHandler : IRequestHandler<GetFeatureFlagStatusQuery, FeatureFlagResponse>
{
    private readonly IPlanPolicyService _planPolicyService;

    /// <summary>Initializes a new instance.</summary>
    public GetFeatureFlagStatusQueryHandler(IPlanPolicyService planPolicyService)
    {
        _planPolicyService = planPolicyService;
    }

    /// <inheritdoc />
    public async Task<FeatureFlagResponse> Handle(GetFeatureFlagStatusQuery request, CancellationToken cancellationToken)
    {
        var enabled = await _planPolicyService.IsFeatureEnabledAsync(request.TenantId, request.FlagKey, cancellationToken).ConfigureAwait(false);
        return new FeatureFlagResponse
        {
            FlagKey = request.FlagKey,
            Enabled = enabled,
            RolloutPercentage = 100 // fallback default evaluation
        };
    }
}

// ─── GET /admin/tenants/{id}/flags ────────────────────────────────

/// <summary>Query to retrieve all configured feature flags for a specific tenant.</summary>
public sealed record GetTenantFeatureFlagsQuery(Guid TenantId) : IRequest<List<FeatureFlagResponse>>;

/// <summary>Handler for GetTenantFeatureFlagsQuery.</summary>
public sealed class GetTenantFeatureFlagsQueryHandler : IRequestHandler<GetTenantFeatureFlagsQuery, List<FeatureFlagResponse>>
{
    private readonly IDbConnectionFactory _dbFactory;

    /// <summary>Initializes a new instance.</summary>
    public GetTenantFeatureFlagsQueryHandler(IDbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <inheritdoc />
    public async Task<List<FeatureFlagResponse>> Handle(GetTenantFeatureFlagsQuery request, CancellationToken cancellationToken)
    {
        using var connection = await _dbFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        
        // RULE 4: Always scope queries by tenant_id!
        var records = await connection.QueryAsync<FeatureFlagRecord>(
            "SELECT flag_key, enabled, rollout_percentage FROM feature_flags WHERE tenant_id = @TenantId",
            new { TenantId = request.TenantId.ToString() }).ConfigureAwait(false);

        return records.Select(r => new FeatureFlagResponse
        {
            FlagKey = r.Flag_key,
            Enabled = r.Enabled,
            RolloutPercentage = r.Rollout_percentage
        }).ToList();
    }

    private sealed record FeatureFlagRecord
    {
        public required string Flag_key { get; init; }
        public required bool Enabled { get; init; }
        public required int Rollout_percentage { get; init; }
    }
}

// ─── PUT /admin/tenants/{id}/flags/{key} ──────────────────────────

/// <summary>Command to create or update a feature flag override for a tenant.</summary>
public sealed record UpdateTenantFeatureFlagCommand(
    Guid TenantId,
    string FlagKey,
    bool Enabled,
    int RolloutPercentage) : IRequest;

/// <summary>Handler for UpdateTenantFeatureFlagCommand.</summary>
public sealed class UpdateTenantFeatureFlagCommandHandler : IRequestHandler<UpdateTenantFeatureFlagCommand>
{
    private readonly IDbConnectionFactory _dbFactory;
    private readonly IConnectionMultiplexer? _redis;

    /// <summary>Initializes a new instance.</summary>
    public UpdateTenantFeatureFlagCommandHandler(IDbConnectionFactory dbFactory, IConnectionMultiplexer? redis)
    {
        _dbFactory = dbFactory;
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task Handle(UpdateTenantFeatureFlagCommand request, CancellationToken cancellationToken)
    {
        using var connection = await _dbFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

        // Dialect-agnostic SELECT EXISTS + INSERT/UPDATE
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM feature_flags WHERE tenant_id = @TenantId AND flag_key = @FlagKey)",
            new { TenantId = request.TenantId.ToString(), FlagKey = request.FlagKey }).ConfigureAwait(false);

        if (exists)
        {
            await connection.ExecuteAsync(
                @"UPDATE feature_flags 
                  SET enabled = @Enabled, rollout_percentage = @RolloutPercentage 
                  WHERE tenant_id = @TenantId AND flag_key = @FlagKey",
                new
                {
                    TenantId = request.TenantId.ToString(),
                    FlagKey = request.FlagKey,
                    request.Enabled,
                    request.RolloutPercentage
                }).ConfigureAwait(false);
        }
        else
        {
            await connection.ExecuteAsync(
                @"INSERT INTO feature_flags (id, tenant_id, flag_key, enabled, rollout_percentage, created_at)
                  VALUES (@Id, @TenantId, @FlagKey, @Enabled, @RolloutPercentage, @CreatedAt)",
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    TenantId = request.TenantId.ToString(),
                    FlagKey = request.FlagKey,
                    request.Enabled,
                    request.RolloutPercentage,
                    CreatedAt = DateTimeOffset.UtcNow
                }).ConfigureAwait(false);
        }

        // Cache Busting: delete cached key in Redis
        if (_redis is not null && _redis.IsConnected)
        {
            var cacheKey = $"saas:feature:{request.TenantId}:{request.FlagKey}";
            await _redis.GetDatabase().KeyDeleteAsync(cacheKey).ConfigureAwait(false);
        }
    }
}
