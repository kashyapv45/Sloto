using Hangfire.Client;
using Hangfire.Server;
using Microsoft.Extensions.Logging;

namespace SaasEngine.Api.Infrastructure.BackgroundJobs;

/// <summary>
/// A Hangfire filter to log job execution state and trace tenant boundaries.
/// </summary>
public sealed class HangfireTenantFilter : IClientFilter, IServerFilter
{
    private readonly ILogger<HangfireTenantFilter> _logger;

    /// <summary>
    /// Initializes a new instance of HangfireTenantFilter.
    /// </summary>
    public HangfireTenantFilter(ILogger<HangfireTenantFilter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void OnCreating(CreatingContext filterContext)
    {
        _logger.LogDebug("Creating background job: {JobMethod}", filterContext.Job.Method.Name);
    }

    /// <inheritdoc />
    public void OnCreated(CreatedContext filterContext)
    {
        _logger.LogDebug("Created background job: {JobId}", filterContext.BackgroundJob?.Id);
    }

    /// <inheritdoc />
    public void OnPerforming(PerformingContext filterContext)
    {
        _logger.LogDebug("Performing background job: {JobId}", filterContext.BackgroundJob?.Id);
    }

    /// <inheritdoc />
    public void OnPerformed(PerformedContext filterContext)
    {
        _logger.LogDebug("Performed background job: {JobId}, HasException={HasException}", 
            filterContext.BackgroundJob?.Id, filterContext.Exception is not null);
    }
}
