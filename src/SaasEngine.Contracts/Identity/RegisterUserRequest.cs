namespace SaasEngine.Contracts.Identity;

/// <summary>Request to register a new user in the system.</summary>
public sealed record RegisterUserRequest
{
    /// <summary>Gets the user's email address.</summary>
    public required string Email { get; init; }

    /// <summary>Gets the user's full name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the user's plain text password.</summary>
    public required string Password { get; init; }

    /// <summary>Gets the user's role (e.g., admin, member).</summary>
    public required string Role { get; init; }
}
