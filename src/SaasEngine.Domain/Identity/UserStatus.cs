namespace SaasEngine.Domain.Identity;

/// <summary>
/// Defines the lifecycle status of a user.
/// </summary>
public enum UserStatus
{
    /// <summary>User is active.</summary>
    Active = 0,

    /// <summary>User has been soft-deleted.</summary>
    Deleted = 1,

    /// <summary>User's PII has been erased per GDPR Article 17.</summary>
    Erased = 2
}
