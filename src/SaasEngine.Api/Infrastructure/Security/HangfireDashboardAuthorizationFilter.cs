using Hangfire.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SaasEngine.Api.Infrastructure.Security;

/// <summary>
/// Authorizes access to the Hangfire Dashboard.
/// Restricts access to authenticated administrators or local development requests.
/// </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    /// <inheritdoc />
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext is null) return false;

        // Allow localhost development if explicitly configured
        var env = httpContext.RequestServices.GetService<IHostEnvironment>();
        if (env is not null && env.IsDevelopment())
        {
            var isLocal = httpContext.Connection.RemoteIpAddress is null 
                || System.Net.IPAddress.IsLoopback(httpContext.Connection.RemoteIpAddress);
            if (isLocal) return true;
        }

        // Production: require authenticated administrator
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var role = httpContext.User.FindFirst("role")?.Value 
                ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            return string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(role, "system_admin", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
