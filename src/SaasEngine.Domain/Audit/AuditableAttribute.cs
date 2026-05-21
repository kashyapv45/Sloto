using System;

namespace SaasEngine.Domain.Audit;

/// <summary>
/// Decorator attribute for requests that should be automatically audited by the pipeline.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class AuditableAttribute : Attribute
{
    /// <summary>Gets the action name (e.g. 'Register', 'ChangePlan').</summary>
    public string Action { get; }

    /// <summary>Gets the type of resource affected (e.g. 'User', 'Tenant').</summary>
    public string ResourceType { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AuditableAttribute"/>.
    /// </summary>
    public AuditableAttribute(string action, string resourceType)
    {
        Action = action;
        ResourceType = resourceType;
    }
}
