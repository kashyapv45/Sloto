using SaasEngine.Domain.Shared;

namespace SaasEngine.Domain.Tenancy;

/// <summary>
/// Thrown when the tenant context cannot be resolved from the current request.
/// </summary>
public sealed class TenantNotResolvedException : DomainException
{
    /// <summary>Initializes a new instance of <see cref="TenantNotResolvedException"/>.</summary>
    public TenantNotResolvedException()
        : base("Tenant could not be resolved from the current request context.") { }

    /// <summary>Initializes a new instance with a custom message.</summary>
    /// <param name="message">The error message.</param>
    public TenantNotResolvedException(string message)
        : base(message) { }
}
