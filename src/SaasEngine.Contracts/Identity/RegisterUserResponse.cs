namespace SaasEngine.Contracts.Identity;

/// <summary>Response from user registration.</summary>
public sealed record RegisterUserResponse : SaasEngine.Domain.Audit.IAuditableResponse
{
    /// <summary>Gets the unique identifier of the registered user.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the user's email address.</summary>
    public required string Email { get; init; }

    /// <summary>Gets the user's full name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the user's role.</summary>
    public required string Role { get; init; }

    /// <inheritdoc />
    Guid? SaasEngine.Domain.Audit.IAuditableResponse.ResourceId => Id;
}
