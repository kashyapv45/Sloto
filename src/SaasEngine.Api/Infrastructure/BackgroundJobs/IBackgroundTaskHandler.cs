using System.Threading;
using System.Threading.Tasks;

namespace SaasEngine.Api.Infrastructure.BackgroundJobs;

/// <summary>
/// Contract for reflection-free AOT-compatible background tasks.
/// </summary>
public interface IBackgroundTaskHandler
{
    /// <summary>
    /// Gets the unique type identifier for this job handler.
    /// </summary>
    string JobType { get; }

    /// <summary>
    /// Executes the background task with the specified payload.
    /// </summary>
    /// <param name="payload">The serialized task payload.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    Task ExecuteAsync(string payload, CancellationToken cancellationToken);
}
