namespace SaasEngine.Contracts.Identity;

/// <summary>Request to authenticate using an MFA code and a challenge token.</summary>
public sealed record MfaLoginRequest
{
    /// <summary>Gets the TOTP code from the authenticator app.</summary>
    public required string Code { get; init; }

    /// <summary>Gets the temporary MFA challenge token returned from the initial login.</summary>
    public required string ChallengeToken { get; init; }
}
