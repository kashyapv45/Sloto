namespace SaasEngine.Contracts.Identity;

/// <summary>Request to verify MFA enrollment.</summary>
public sealed record MfaVerifyRequest
{
    /// <summary>Gets the TOTP code to verify.</summary>
    public required string Code { get; init; }
}
