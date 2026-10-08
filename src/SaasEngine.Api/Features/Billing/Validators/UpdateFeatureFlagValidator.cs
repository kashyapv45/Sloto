using FluentValidation;
using SaasEngine.Contracts.Billing;

namespace SaasEngine.Api.Features.Billing.Validators;

/// <summary>Validates UpdateFeatureFlagRequest numerical bounds and inputs.</summary>
public sealed class UpdateFeatureFlagValidator : AbstractValidator<UpdateFeatureFlagRequest>
{
    /// <summary>Initializes numerical bounds validation rules.</summary>
    public UpdateFeatureFlagValidator()
    {
        RuleFor(x => x.RolloutPercentage)
            .InclusiveBetween(0, 100)
            .WithMessage("Rollout percentage must be between 0 and 100.");
    }
}
