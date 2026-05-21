namespace SaasEngine.Domain.Identity;

/// <summary>
/// Represents a user within a tenant. Immutable domain entity.
/// </summary>
public sealed record User
{
    /// <summary>Gets the unique user identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant this user belongs to.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the user's email address.</summary>
    public required string Email { get; init; }

    /// <summary>Gets the user's display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the user's role within the tenant.</summary>
    public required string Role { get; init; }

    /// <summary>Gets the hashed password.</summary>
    public required string PasswordHash { get; init; }

    /// <summary>Gets the encrypted TOTP MFA secret, if MFA is enrolled.</summary>
    public string? MfaSecret { get; init; }

    /// <summary>Gets whether MFA is enabled for this user.</summary>
    public bool MfaEnabled { get; init; }

    /// <summary>Gets the user's status.</summary>
    public required UserStatus Status { get; init; }

    /// <summary>Gets the UTC timestamp when this user was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
