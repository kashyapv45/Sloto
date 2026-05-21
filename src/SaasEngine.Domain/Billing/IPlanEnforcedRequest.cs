using System;
using System.Collections.Generic;

namespace SaasEngine.Domain.Billing;

/// <summary>
/// Defines the type of plan policy to enforce.
/// </summary>
public enum PlanPolicyType
{
    /// <summary>Enforces maximum active user seat limits.</summary>
    Seats,

    /// <summary>Enforces API request rate limiting per minute.</summary>
    ApiRate,

    /// <summary>Checks tenant-specific feature flag access.</summary>
    Feature
}

/// <summary>
/// Describes a single plan enforcement policy to be applied to a request.
/// </summary>
/// <param name="Policy">The policy type to enforce.</param>
/// <param name="FeatureKey">The optional feature key (only applicable for <see cref="PlanPolicyType.Feature"/>).</param>
public sealed record PlanEnforcementPolicy(PlanPolicyType Policy, string? FeatureKey = null);

/// <summary>
/// Interface implemented by MediatR requests that require plan enforcement.
/// Provides plan policies without requiring runtime reflection on attributes.
/// </summary>
public interface IPlanEnforcedRequest
{
    /// <summary>Gets the plan enforcement policies to apply before executing this request.</summary>
    IReadOnlyList<PlanEnforcementPolicy> PlanPolicies { get; }
}
