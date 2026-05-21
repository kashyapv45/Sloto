using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Infrastructure.BackgroundJobs;

/// <summary>
/// A statically defined single dispatcher for executing background jobs under the correct tenant scope.
/// Fully compatible with Native AOT compilation.
/// </summary>
public sealed class BackgroundTaskDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAdminConnectionFactory _adminDb;
    private readonly ILogger<BackgroundTaskDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of BackgroundTaskDispatcher.
    /// </summary>
    public BackgroundTaskDispatcher(
        IServiceProvider serviceProvider,
        IAdminConnectionFactory adminDb,
        ILogger<BackgroundTaskDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _adminDb = adminDb;
        _logger = logger;
    }

    /// <summary>
    /// Dispatches a serialized background job to the appropriate handler within a scoped tenant context.
    /// </summary>
    /// <param name="jobType">The identifier of the task worker type.</param>
    /// <param name="payload">The serialized task arguments.</param>
    /// <param name="tenantId">The tenant scope executing the job.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    public async Task DispatchAsync(string jobType, string payload, Guid tenantId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Dispatching background job of type {JobType} for tenant {TenantId}.", jobType, tenantId);

        // 1. Query tenant tier and status from admin DB to get latest state
        string tier;
        using (var adminConnection = await _adminDb.CreateAsync(cancellationToken).ConfigureAwait(false))
        {
            var tenant = await adminConnection.QuerySingleOrDefaultAsync<TenantDbRecord>(
                "SELECT tier, status FROM tenants WHERE id = @TenantId",
                new { TenantId = tenantId.ToString() }).ConfigureAwait(false);

            if (tenant is null)
            {
                _logger.LogError("Tenant {TenantId} not found in admin database. Skipping background job.", tenantId);
                return;
            }

            if (!tenant.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Skipping background job for tenant {TenantId} because tenant status is: {Status}.", tenantId, tenant.Status);
                return;
            }

            tier = tenant.Tier;
        }

        // 2. Resolve handler and execute inside scoped tenant container
        using var scope = _serviceProvider.CreateScope();
        
        var tenantService = scope.ServiceProvider.GetRequiredService<ITenantService>();
        tenantService.SetTenantContext(tenantId, tier);

        var handlers = scope.ServiceProvider.GetServices<IBackgroundTaskHandler>();
        var handler = handlers.FirstOrDefault(h => h.JobType.Equals(jobType, StringComparison.OrdinalIgnoreCase));

        if (handler is null)
        {
            _logger.LogError("No background task handler registered for job type: {JobType}.", jobType);
            throw new InvalidOperationException($"No background task handler found for job type: {jobType}");
        }

        try
        {
            await handler.ExecuteAsync(payload, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Successfully completed background job of type {JobType} for tenant {TenantId}.", jobType, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during execution of background job {JobType} for tenant {TenantId}.", jobType, tenantId);
            throw; // Propagate exception to allow Hangfire to retry
        }
    }

    private sealed record TenantDbRecord(string Tier, string Status);
}
