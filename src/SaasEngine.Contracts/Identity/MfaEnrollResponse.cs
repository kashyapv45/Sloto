namespace SaasEngine.Contracts.Identity;

/// <summary>Response containing MFA enrollment details.</summary>
public sealed record MfaEnrollResponse
{
    /// <summary>Gets the TOTP secret for manual entry.</summary>
    public required string Secret { get; init; }

    /// <summary>Gets the QR code data URL for authenticator app scanning.</summary>
    public required string QrCodeDataUrl { get; init; }
}
