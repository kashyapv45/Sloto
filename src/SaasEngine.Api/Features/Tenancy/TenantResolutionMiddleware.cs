using Dapper;
using SaasEngine.Api.Infrastructure.Data;
using SaasEngine.Domain.Shared;
using SaasEngine.Domain.Tenancy;

namespace SaasEngine.Api.Features.Tenancy;

/// <summary>
/// Middleware that validates tenant context on every request.
/// Rejects requests with 401 if tenant not found, 403 if suspended.
/// Skips validation for health check and auth endpoints.
/// Implements <see cref="IMiddleware"/> for AOT-compatible strongly-typed invocation.
/// </summary>
public sealed class TenantResolutionMiddleware : IMiddleware
{
    private static readonly HashSet<string> SkipPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health/live",
        "/health/ready",
        "/health/startup",
        "/auth/token"
    };

    private readonly IAdminConnectionFactory _adminDb;

    /// <summary>Initializes a new instance.</summary>
    public TenantResolutionMiddleware(IAdminConnectionFactory adminDb)
    {
        _adminDb = adminDb;
    }

    /// <summary>Processes the HTTP request.</summary>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip tenant validation for health/auth endpoints
        if (SkipPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Skip if no authenticated user (let auth middleware handle it)
        var tidClaim = context.User.FindFirst("tid")?.Value;
        if (string.IsNullOrEmpty(tidClaim))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(tidClaim, out var tenantId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Verify tenant exists and is active
        using var connection = await _adminDb.CreateAsync(context.RequestAborted).ConfigureAwait(false);
        var tenant = await connection.QuerySingleOrDefaultAsync<TenantStatusRecord>(
            "SELECT status FROM tenants WHERE id = @tenantId",
            new { tenantId }).ConfigureAwait(false);

        Serilog.Log.Information("Tenant resolution: queried ID={TenantId}, Found={Found}, Status={Status}", 
            tenantId, tenant is not null, tenant?.Status);

        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (string.Equals(tenant.Status, "suspended", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private sealed record TenantStatusRecord
    {
        public required string Status { get; init; }
    }
}
