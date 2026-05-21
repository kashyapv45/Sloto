using FluentValidation;
using SaasEngine.Contracts.Tenancy;

namespace SaasEngine.Api.Features.Tenancy.Validators;

/// <summary>Validates CreateTenantRequest.</summary>
public sealed class CreateTenantValidator : AbstractValidator<CreateTenantRequest>
{
    private static readonly HashSet<string> ValidTiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "standard", "professional", "enterprise"
    };

    /// <summary>Initializes validation rules.</summary>
    public CreateTenantValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tenant name is required.")
            .MaximumLength(200).WithMessage("Tenant name must not exceed 200 characters.");

        RuleFor(x => x.Tier)
            .NotEmpty().WithMessage("Tier is required.")
            .Must(tier => ValidTiers.Contains(tier))
            .WithMessage("Tier must be one of: standard, professional, enterprise.");

        RuleFor(x => x.PlanKey)
            .NotEmpty().WithMessage("Plan key is required.");

        RuleFor(x => x.ConnectionSecretRef)
            .NotEmpty()
            .When(x => string.Equals(x.Tier, "enterprise", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Connection secret reference is required for enterprise tier.");
    }
}
