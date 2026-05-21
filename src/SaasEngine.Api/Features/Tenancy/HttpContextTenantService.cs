using System.Security.Claims;
using SaasEngine.Domain.Billing;
using SaasEngine.Domain.Shared;
using SaasEngine.Domain.Tenancy;

namespace SaasEngine.Api.Features.Tenancy;

/// <summary>
/// Resolves tenant context from the current HTTP request JWT claims.
/// Registered as Scoped in DI container.
/// </summary>
public sealed class HttpContextTenantService : ITenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantConnectionResolver _connectionResolver;
    private Guid? _tenantId;
    private string? _tier;
    private PlanInfo? _plan;

    /// <summary>Initializes a new instance.</summary>
    public HttpContextTenantService(
        IHttpContextAccessor httpContextAccessor,
        ITenantConnectionResolver connectionResolver)
    {
        _httpContextAccessor = httpContextAccessor;
        _connectionResolver = connectionResolver;
    }

    /// <inheritdoc />
    public Guid CurrentTenantId
    {
        get
        {
            if (_tenantId.HasValue) return _tenantId.Value;

            var tidClaim = _httpContextAccessor.HttpContext?.User?.FindFirstValue("tid");
            if (string.IsNullOrEmpty(tidClaim) || !Guid.TryParse(tidClaim, out var tenantId))
            {
                throw new TenantNotResolvedException();
            }

            _tenantId = tenantId;
            return _tenantId.Value;
        }
    }

    /// <inheritdoc />
    public string CurrentTier
    {
        get
        {
            if (_tier is not null) return _tier;

            _tier = _httpContextAccessor.HttpContext?.User?.FindFirstValue("tier") ?? "standard";
            return _tier;
        }
    }

    /// <inheritdoc />
    public string GetConnectionString()
    {
        return _connectionResolver.Resolve(CurrentTenantId, CurrentTier);
    }

    /// <inheritdoc />
    public PlanInfo GetPlan()
    {
        if (_plan is not null) return _plan;

        var planClaim = _httpContextAccessor.HttpContext?.User?.FindFirstValue("plan");
        _plan = new PlanInfo
        {
            Key = planClaim ?? "starter",
            MaxSeats = 5,
            MaxApiCallsPerMinute = 60,
            Features = Array.Empty<string>()
        };
        return _plan;
    }

    /// <inheritdoc />
    public void SetTenantContext(Guid tenantId, string tier)
    {
        _tenantId = tenantId;
        _tier = tier;
    }
}
