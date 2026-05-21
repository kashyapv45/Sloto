namespace SaasEngine.Contracts.Identity;

/// <summary>Request to authenticate and obtain a JWT token.</summary>
public sealed record AuthTokenRequest
{
    /// <summary>Gets the user's email address.</summary>
    public required string Email { get; init; }

    /// <summary>Gets the user's password.</summary>
    public required string Password { get; init; }

    /// <summary>Gets the optional MFA TOTP code.</summary>
    public string? MfaCode { get; init; }
}
