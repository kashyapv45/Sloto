using MediatR;
using SaasEngine.Api.Features.Billing;
using SaasEngine.Domain.Billing;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Features.Billing;

/// <summary>
/// MediatR pipeline behavior enforcing subscription plan policies and feature flags.
/// Uses interface-based dispatch instead of runtime reflection for Native AOT compatibility.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class PlanEnforcementBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IPlanPolicyService _planPolicyService;
    private readonly ITenantService _tenantService;

    /// <summary>
    /// Initializes a new instance of <see cref="PlanEnforcementBehavior{TRequest, TResponse}"/>.
    /// </summary>
    public PlanEnforcementBehavior(
        IPlanPolicyService planPolicyService,
        ITenantService tenantService)
    {
        _planPolicyService = planPolicyService;
        _tenantService = tenantService;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // AOT-safe: interface check instead of GetCustomAttributes reflection
        if (request is not IPlanEnforcedRequest planEnforced)
        {
            return await next().ConfigureAwait(false);
        }

        Guid? tenantId = null;

        foreach (var policy in planEnforced.PlanPolicies)
        {
            if (!tenantId.HasValue)
            {
                tenantId = request is ITenantAwareRequest tenantAware
                    ? tenantAware.TenantId
                    : _tenantService.CurrentTenantId;
            }

            switch (policy.Policy)
            {
                case PlanPolicyType.Seats:
                    await _planPolicyService.EnforceSeatsAsync(tenantId.Value, cancellationToken).ConfigureAwait(false);
                    break;

                case PlanPolicyType.ApiRate:
                    // Use request type name as endpoint indicator for rate limiting
                    var endpoint = typeof(TRequest).Name;
                    await _planPolicyService.EnforceApiRateAsync(tenantId.Value, endpoint, cancellationToken).ConfigureAwait(false);
                    break;

                case PlanPolicyType.Feature:
                    if (string.IsNullOrEmpty(policy.FeatureKey))
                    {
                        throw new InvalidOperationException("FeatureKey must be specified when using PlanPolicyType.Feature.");
                    }
                    var enabled = await _planPolicyService.IsFeatureEnabledAsync(tenantId.Value, policy.FeatureKey, cancellationToken).ConfigureAwait(false);
                    if (!enabled)
                    {
                        throw new FeatureDisabledException(policy.FeatureKey);
                    }
                    break;
            }
        }

        return await next().ConfigureAwait(false);
    }
}
