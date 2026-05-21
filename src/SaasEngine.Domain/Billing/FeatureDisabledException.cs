using SaasEngine.Domain.Shared;

namespace SaasEngine.Domain.Billing;

/// <summary>
/// Thrown when a tenant attempts to use a disabled feature.
/// </summary>
public sealed class FeatureDisabledException : DomainException
{
    /// <summary>Gets the feature key that is disabled.</summary>
    public string FeatureKey { get; }

    /// <summary>Initializes a new instance of <see cref="FeatureDisabledException"/>.</summary>
    /// <param name="featureKey">The disabled feature key.</param>
    public FeatureDisabledException(string featureKey)
        : base($"Feature '{featureKey}' is not enabled for this tenant.")
    {
        FeatureKey = featureKey;
    }
}
