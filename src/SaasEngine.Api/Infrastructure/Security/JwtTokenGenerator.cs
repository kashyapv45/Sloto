using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SaasEngine.Api.Infrastructure.Security;

/// <summary>
/// Provides secure JWT token generation using RS256 asymmetric signing.
/// </summary>
public sealed class JwtTokenGenerator
{
    private readonly RSA _rsaPrivateKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly RsaSecurityKey _signingKey;
    private readonly RsaSecurityKey _publicKey;

    /// <summary>
    /// Initializes a new instance of the JwtTokenGenerator.
    /// </summary>
    public JwtTokenGenerator(
        string? privateKeyPem = null, 
        string issuer = "SaasEngine", 
        string audience = "SaasEngine.Api")
    {
        _issuer = issuer;
        _audience = audience;

        if (!string.IsNullOrWhiteSpace(privateKeyPem))
        {
            _rsaPrivateKey = RSA.Create();
            _rsaPrivateKey.ImportFromPem(privateKeyPem);
        }
        else
        {
            throw new ArgumentException("A valid RSA private key in PEM format is required.", nameof(privateKeyPem));
        }

        _signingKey = new RsaSecurityKey(_rsaPrivateKey) { KeyId = "saasengine-jwt-key" };

        // Extract public parameters only for validation
        var publicParameters = _rsaPrivateKey.ExportParameters(false);
        var rsaPublic = RSA.Create();
        rsaPublic.ImportParameters(publicParameters);
        _publicKey = new RsaSecurityKey(rsaPublic) { KeyId = "saasengine-jwt-key" };
    }

    /// <summary>
    /// Gets the public key to configure the token validation parameters.
    /// </summary>
    public RsaSecurityKey GetPublicKey()
    {
        return _publicKey;
    }

    /// <summary>
    /// Generates a signed JWT access token or MFA challenge token.
    /// </summary>
    public string GenerateToken(
        Guid userId, 
        Guid tenantId, 
        string tier, 
        string role, 
        int expirationMinutes = 60,
        bool mfaPending = false)
    {
        var tokenHandler = new JsonWebTokenHandler();
        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);

        var claims = new Dictionary<string, object>
        {
            { JwtRegisteredClaimNames.Sub, userId.ToString() },
            { JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString() },
            { "tid", tenantId.ToString() },
            { "tier", tier },
            { "role", role }
        };

        if (mfaPending)
        {
            claims.Add("mfa_pending", "true");
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = credentials
        };

        return tokenHandler.CreateToken(descriptor);
    }
}
