namespace SaasEngine.Contracts.Identity;

/// <summary>Response from authentication operations containing tokens or MFA challenges.</summary>
public sealed record AuthResponse
{
    /// <summary>Gets the JWT access token (null if MFA is required).</summary>
    public string? AccessToken { get; init; }

    /// <summary>Gets the token expiration in seconds (null if MFA is required).</summary>
    public int? ExpiresIn { get; init; }

    /// <summary>Gets the token type (always 'Bearer').</summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary>Gets a value indicating whether MFA verification is required.</summary>
    public bool MfaRequired { get; init; }

    /// <summary>Gets the temporary MFA challenge token to be used in /auth/mfa/verify.</summary>
    public string? ChallengeToken { get; init; }
}
