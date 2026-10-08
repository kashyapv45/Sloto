using Dapper;
using FluentValidation;
using SaasEngine.Api.Infrastructure.Data;
using SaasEngine.Api.Infrastructure.Security;
using SaasEngine.Contracts.Tenancy;
using SaasEngine.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace SaasEngine.Api.Features.Tenancy.Endpoints;

/// <summary>
/// Maps admin tenant lifecycle endpoints.
/// Protected by AdminEndpointFilter (X-Internal-Key or Admin role).
/// </summary>
public static class TenantEndpoints
{
    /// <summary>Maps all tenant admin endpoints under /admin/tenants.</summary>
    public static void MapTenantEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/tenants")
            .WithTags("Tenants")
            .AddEndpointFilter<AdminEndpointFilter>();

        group.MapPost("/", CreateTenant);
        group.MapGet("/{id:guid}", GetTenant);
        group.MapPatch("/{id:guid}/status", UpdateTenantStatus);
        group.MapPatch("/{id:guid}/plan", UpdateTenantPlan);
        group.MapDelete("/{id:guid}", DeleteTenant);
    }

    /// <summary>Provisions a new tenant.</summary>
    private static async Task<IResult> CreateTenant(
        CreateTenantRequest request,
        [FromServices] IValidator<CreateTenantRequest> validator,
        [FromServices] IAdminConnectionFactory adminDb,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(validationResult.ToDictionary());
        }
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using var connection = await adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);

        // Look up the plan by key
        var plan = await connection.QuerySingleOrDefaultAsync<PlanRecord>(
            "SELECT id FROM plans WHERE key = @Key",
            new { Key = request.PlanKey }).ConfigureAwait(false);

        var planId = plan?.Id ?? Guid.NewGuid(); // fallback for initial setup before plans seeded

        await connection.ExecuteAsync(
            @"INSERT INTO tenants (id, name, tier, plan_id, status, connection_secret_ref, created_at, updated_at)
              VALUES (@Id, @Name, @Tier, @PlanId, 'active', @ConnectionSecretRef, @CreatedAt, @CreatedAt)",
            new
            {
                Id = id,
                request.Name,
                Tier = request.Tier.ToLowerInvariant(),
                PlanId = planId,
                request.ConnectionSecretRef,
                CreatedAt = now
            }).ConfigureAwait(false);

        var response = new TenantResponse
        {
            Id = id,
            Name = request.Name,
            Tier = request.Tier,
            PlanId = planId,
            Status = "active",
            CreatedAt = now
        };

        return Results.Created($"/admin/tenants/{id}", response);
    }

    /// <summary>Gets tenant details by ID.</summary>
    private static async Task<IResult> GetTenant(
        Guid id,
        [FromServices] IAdminConnectionFactory adminDb,
        CancellationToken cancellationToken)
    {
        using var connection = await adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);

        var tenant = await connection.QuerySingleOrDefaultAsync<TenantRecord>(
            "SELECT id, name, tier, plan_id, status, created_at, updated_at FROM tenants WHERE id = @Id",
            new { Id = id }).ConfigureAwait(false);

        if (tenant is null)
        {
            return Results.Problem(
                title: "Tenant not found",
                detail: $"Tenant '{id}' was not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(new TenantResponse
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Tier = tenant.Tier,
            PlanId = tenant.Plan_id,
            Status = tenant.Status,
            CreatedAt = tenant.Created_at,
            UpdatedAt = tenant.Updated_at
        });
    }

    /// <summary>Updates tenant status (active/suspended).</summary>
    private static async Task<IResult> UpdateTenantStatus(
        Guid id,
        UpdateTenantStatusRequest request,
        [FromServices] IAdminConnectionFactory adminDb,
        CancellationToken cancellationToken)
    {
        var statusLower = request.Status?.ToLowerInvariant() ?? string.Empty;
        if (statusLower != "active" && statusLower != "suspended" && statusLower != "deleted")
        {
            return Results.Problem(
                title: "Invalid Status",
                detail: "Status must be one of: active, suspended, deleted.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        using var connection = await adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.ExecuteAsync(
            "UPDATE tenants SET status = @Status, updated_at = @Now WHERE id = @Id",
            new { Id = id, Status = statusLower, Now = DateTimeOffset.UtcNow }).ConfigureAwait(false);

        if (rows == 0)
        {
            return Results.Problem(
                title: "Tenant not found",
                detail: $"Tenant '{id}' was not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.NoContent();
    }

    /// <summary>Changes a tenant's plan.</summary>
    private static async Task<IResult> UpdateTenantPlan(
        Guid id,
        UpdateTenantPlanRequest request,
        [FromServices] IAdminConnectionFactory adminDb,
        [FromServices] IEventBus eventBus,
        CancellationToken cancellationToken)
    {
        using var connection = await adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);

        var oldPlanId = await connection.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT plan_id FROM tenants WHERE id = @Id",
            new { Id = id }).ConfigureAwait(false);

        var plan = await connection.QuerySingleOrDefaultAsync<PlanRecord>(
            "SELECT id FROM plans WHERE key = @Key",
            new { Key = request.PlanKey }).ConfigureAwait(false);

        if (plan is null)
        {
            return Results.Problem(
                title: "Plan not found",
                detail: $"Plan '{request.PlanKey}' was not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var rows = await connection.ExecuteAsync(
            "UPDATE tenants SET plan_id = @PlanId, updated_at = @Now WHERE id = @Id",
            new { Id = id, PlanId = plan.Id, Now = DateTimeOffset.UtcNow }).ConfigureAwait(false);

        if (rows == 0)
        {
            return Results.Problem(
                title: "Tenant not found",
                detail: $"Tenant '{id}' was not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (oldPlanId.HasValue && oldPlanId.Value != plan.Id)
        {
            await eventBus.PublishAsync(new SaasEngine.Domain.Tenancy.Events.PlanChangedEvent
            {
                TenantId = id,
                OldPlanId = oldPlanId.Value,
                NewPlanId = plan.Id
            }, cancellationToken).ConfigureAwait(false);
        }

        return Results.NoContent();
    }

    /// <summary>Soft-deletes a tenant.</summary>
    private static async Task<IResult> DeleteTenant(
        Guid id,
        [FromServices] IAdminConnectionFactory adminDb,
        CancellationToken cancellationToken)
    {
        using var connection = await adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.ExecuteAsync(
            "UPDATE tenants SET status = 'deleted', updated_at = @Now WHERE id = @Id",
            new { Id = id, Now = DateTimeOffset.UtcNow }).ConfigureAwait(false);

        if (rows == 0)
        {
            return Results.Problem(
                title: "Tenant not found",
                detail: $"Tenant '{id}' was not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Accepted();
    }

    // Internal Dapper mapping records
    private sealed record TenantRecord
    {
        public required Guid Id { get; init; }
        public required string Name { get; init; }
        public required string Tier { get; init; }
        public required Guid Plan_id { get; init; }
        public required string Status { get; init; }
        public required DateTimeOffset Created_at { get; init; }
        public DateTimeOffset? Updated_at { get; init; }
    }

    private sealed record PlanRecord
    {
        public required Guid Id { get; init; }
    }
}
