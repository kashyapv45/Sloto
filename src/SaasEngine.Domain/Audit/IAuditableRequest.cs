using System;

namespace SaasEngine.Domain.Audit;

/// <summary>
/// Interface implemented by requests that should be automatically audited.
/// Provides audit metadata and the target resource ID without requiring runtime reflection.
/// </summary>
public interface IAuditableRequest
{
    /// <summary>Gets the audit action name (e.g. "Register", "ChangePlan").</summary>
    string AuditAction { get; }

    /// <summary>Gets the type of resource affected (e.g. "User", "Tenant").</summary>
    string AuditResourceType { get; }

    /// <summary>Gets the target resource identifier, if known before execution.</summary>
    Guid? ResourceId { get; }
}

/// <summary>
/// Interface that can be implemented by responses to expose the target resource ID
/// for audit events where the resource is created during execution.
/// </summary>
public interface IAuditableResponse
{
    /// <summary>Gets the target resource identifier.</summary>
    Guid? ResourceId { get; }
}
