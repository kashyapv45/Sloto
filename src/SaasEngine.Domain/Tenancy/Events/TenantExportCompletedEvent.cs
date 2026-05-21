using System;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Domain.Tenancy.Events;

/// <summary>
/// Raised when a tenant's complete data export is successfully completed.
/// </summary>
public sealed record TenantExportCompletedEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the tenant identifier.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the absolute or secure URI path to the generated ZIP archive.</summary>
    public required string ZipPath { get; init; }

    /// <summary>Gets the size of the ZIP archive in bytes.</summary>
    public required long TotalBytes { get; init; }

    /// <summary>Gets the export event identifier for audit linkage.</summary>
    public required Guid ExportEventId { get; init; }
}
