namespace SaasEngine.Contracts.Identity;

/// <summary>Response containing a JWT access token.</summary>
public sealed record AuthTokenResponse
{
    /// <summary>Gets the JWT access token.</summary>
    public required string AccessToken { get; init; }

    /// <summary>Gets the token expiration in seconds.</summary>
    public required int ExpiresIn { get; init; }

    /// <summary>Gets the token type (always 'Bearer').</summary>
    public string TokenType { get; init; } = "Bearer";
}
