using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SaasEngine.Contracts.Billing;
using SaasEngine.Domain.Shared;
using SaasEngine.Api.Infrastructure.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace SaasEngine.Api.Features.Billing;

/// <summary>
/// Maps feature flag check and admin flag management Minimal API endpoints.
/// </summary>
public static class BillingEndpoints
{
    /// <summary>Maps all billing and feature flag endpoints.</summary>
    public static void MapBillingEndpoints(this WebApplication app)
    {
        // Client feature check
        app.MapGet("/features/{key}", CheckFeature)
            .RequireAuthorization()
            .RequireRateLimiting("engine-rate-limit")
            .WithTags("Features");

        // Admin feature flag overrides management
        var adminGroup = app.MapGroup("/admin/tenants/{id:guid}/flags")
            .RequireRateLimiting("engine-rate-limit")
            .AddEndpointFilter<SaasEngine.Api.Infrastructure.Security.AdminEndpointFilter>()
            .WithTags("Admin Feature Flags");

        adminGroup.MapGet("/", GetTenantFlags);
        adminGroup.MapPut("/{key}", UpdateTenantFlag);
    }

    private static async Task<IResult> CheckFeature(
        string key,
        [FromServices] IMediator mediator,
        [FromServices] ITenantService tenantService,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.CurrentTenantId;
        var response = await mediator.Send(new GetFeatureFlagStatusQuery(key, tenantId), cancellationToken).ConfigureAwait(false);
        return TypedResults.Json(response, AppJsonSerializerContext.Default.FeatureFlagResponse);
    }

    private static async Task<IResult> GetTenantFlags(
        Guid id,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(new GetTenantFeatureFlagsQuery(id), cancellationToken).ConfigureAwait(false);
        return TypedResults.Json(response, AppJsonSerializerContext.Default.ListFeatureFlagResponse);
    }

    private static async Task<IResult> UpdateTenantFlag(
        Guid id,
        string key,
        UpdateFeatureFlagRequest request,
        [FromServices] FluentValidation.IValidator<UpdateFeatureFlagRequest> validator,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(validationResult.ToDictionary());
        }

        await mediator.Send(new UpdateTenantFeatureFlagCommand(id, key, request.Enabled, request.RolloutPercentage), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
