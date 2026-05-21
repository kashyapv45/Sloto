using System.Security.Cryptography;
using System.Text;

namespace SaasEngine.Api.Infrastructure.Security;

/// <summary>
/// A lightweight, Native AOT-compatible TOTP (RFC 6238) service for two-factor authentication.
/// </summary>
public static class TotpService
{
    private static readonly char[] Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

    /// <summary>
    /// Generates a new 160-bit Base32 encoded TOTP secret key.
    /// </summary>
    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20); // 160-bit secret
        return EncodeBase32(bytes);
    }

    /// <summary>
    /// Generates the standard otpauth:// provisioning URI for Google Authenticator / Authy.
    /// </summary>
    public static string GenerateQrCodeUri(string email, string secret, string issuer = "SaasEngine")
    {
        return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(email)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}";
    }

    /// <summary>
    /// Verifies the TOTP code against the secret key using the standard time window.
    /// </summary>
    public static bool VerifyCode(string secret, string code, TimeSpan? timeWindow = null)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6 || !int.TryParse(code, out _))
            return false;

        byte[] secretBytes;
        try
        {
            secretBytes = DecodeBase32(secret);
        }
        catch
        {
            return false;
        }

        // Validate within +/- 1 step of 30 seconds (total 90 seconds window)
        var window = timeWindow ?? TimeSpan.FromSeconds(30);
        var steps = (int)(window.TotalSeconds / 30);

        var currentStep = GetCurrentStep();

        for (int i = -steps; i <= steps; i++)
        {
            var step = currentStep + i;
            var expectedCode = CalculateTotp(secretBytes, step);
            if (code == expectedCode)
            {
                return true;
            }
        }

        return false;
    }

    private static long GetCurrentStep()
    {
        var unixEpoch = DateTimeOffset.UnixEpoch;
        var elapsed = DateTimeOffset.UtcNow - unixEpoch;
        return (long)(elapsed.TotalSeconds / 30);
    }

    private static string CalculateTotp(byte[] secret, long step)
    {
        var stepBytes = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(stepBytes);
        }

        var data = new byte[8];
        Array.Copy(stepBytes, 0, data, 8 - stepBytes.Length, stepBytes.Length);

        var hash = HMACSHA1.HashData(secret, data);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        var otp = binary % 1000000;
        return otp.ToString("D6");
    }

    private static string EncodeBase32(byte[] data)
    {
        var sb = new StringBuilder((data.Length + 4) / 5 * 8);
        int index = 0, digit = 0;
        int currentByte = 0, nextByte = 0;

        while (index < data.Length)
        {
            currentByte = data[index];
            if (digit > 3)
            {
                if (index + 1 < data.Length)
                {
                    nextByte = data[index + 1];
                }
                else
                {
                    nextByte = 0;
                }

                var val = (currentByte & (0xFF >> digit)) << (digit - 3);
                val |= (nextByte >> (11 - digit));
                sb.Append(Base32Chars[val]);
                index++;
                digit = (digit + 5) % 8;
            }
            else
            {
                var val = (currentByte >> (3 - digit)) & 0x1F;
                sb.Append(Base32Chars[val]);
                digit = (digit + 5) % 8;
                if (digit == 0) index++;
            }
        }

        return sb.ToString();
    }

    private static byte[] DecodeBase32(string base32)
    {
        base32 = base32.Trim().Replace(" ", "").ToUpperInvariant();
        var bytes = new byte[base32.Length * 5 / 8];
        int index = 0, lookup = 0, offset = 0;

        foreach (var c in base32)
        {
            lookup = c - 'A';
            if (lookup < 0 || lookup >= 26)
            {
                lookup = c - '2';
                if (lookup < 0 || lookup >= 6)
                {
                    throw new ArgumentException("Invalid Base32 character");
                }
                lookup += 26;
            }

            if (offset <= 3)
            {
                bytes[index] |= (byte)(lookup << (3 - offset));
                offset += 5;
                if (offset >= 8)
                {
                    offset %= 8;
                    index++;
                }
            }
            else
            {
                bytes[index] |= (byte)(lookup >> (offset - 3));
                index++;
                if (index < bytes.Length)
                {
                    bytes[index] |= (byte)(lookup << (11 - offset));
                }
                offset = (offset + 5) % 8;
            }
        }

        return bytes;
    }
}
