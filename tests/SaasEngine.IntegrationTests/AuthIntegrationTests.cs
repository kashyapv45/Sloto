using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaasEngine.Contracts.Identity;
using SaasEngine.Domain.Shared;
using SaasEngine.Api.Infrastructure.Security;
using Xunit;

namespace SaasEngine.IntegrationTests;

public sealed class AuthIntegrationTests : IClassFixture<WebApplicationFactory<SaasEngine.Api.Program>>
{
    private readonly WebApplicationFactory<SaasEngine.Api.Program> _factory;
    private readonly MockDbConnectionFactory _dbFactory;

    public AuthIntegrationTests(WebApplicationFactory<SaasEngine.Api.Program> factory)
    {
        _dbFactory = new MockDbConnectionFactory();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove production DB factories
                var adminDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IAdminConnectionFactory));
                if (adminDescriptor != null) services.Remove(adminDescriptor);

                var dbDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IDbConnectionFactory));
                if (dbDescriptor != null) services.Remove(dbDescriptor);

                // Register SQLite Mock Connection Factory
                services.AddSingleton<IAdminConnectionFactory>(_dbFactory);
                services.AddSingleton<IDbConnectionFactory>(_dbFactory);

                // Do NOT override AddAuthentication here; use the real JwtBearer scheme 
                // so we can test JWT issuance, signature validation, and Redis blacklisting.
            });
        });
    }

    [Fact]
    public async Task Complete_AuthFlow_Register_Login_Mfa_Logout()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        // 1. Seed Tenant in DB
        using (var connection = await _dbFactory.CreateAsync())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO tenants (id, name, tier, plan_id, status, created_at)
                  VALUES (@Id, 'Auth Test Tenant', 'standard', @PlanId, 'active', @Now)",
                new { Id = tenantId, PlanId = planId, Now = DateTimeOffset.UtcNow });
        }

        var client = _factory.CreateClient();

        // 2. Register User
        var registerRequest = new RegisterUserRequest
        {
            Email = "test@saasengine.com",
            Name = "Test User",
            Password = "SecurePassword123!",
            Role = "admin"
        };

        var registerMsg = new HttpRequestMessage(HttpMethod.Post, "/auth/register")
        {
            Content = JsonContent.Create(registerRequest)
        };
        registerMsg.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var registerResponse = await client.SendAsync(registerMsg);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        // 3. Login User (MFA Disabled initially)
        var loginRequest = new AuthTokenRequest
        {
            Email = "test@saasengine.com",
            Password = "SecurePassword123!"
        };

        var loginMsg = new HttpRequestMessage(HttpMethod.Post, "/auth/token")
        {
            Content = JsonContent.Create(loginRequest)
        };
        loginMsg.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var loginResponse = await client.SendAsync(loginMsg);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authResult);
        Assert.NotNull(authResult.AccessToken);
        Assert.False(authResult.MfaRequired);

        // 4. Enroll in MFA (requires authenticated client)
        var authClient = _factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);

        var enrollResponse = await authClient.PostAsync("/auth/mfa/enroll", null);
        Assert.Equal(HttpStatusCode.OK, enrollResponse.StatusCode);

        var enrollResult = await enrollResponse.Content.ReadFromJsonAsync<MfaEnrollResponse>();
        Assert.NotNull(enrollResult);
        Assert.NotEmpty(enrollResult.Secret);
        Assert.Contains(enrollResult.Secret, enrollResult.QrCodeDataUrl);

        // 5. Enable MFA using verification code
        // Generate a valid code using TotpService
        var validCode = CalculateTotp(enrollResult.Secret);
        var enableRequest = new MfaVerifyRequest
        {
            Code = validCode
        };

        var enableResponse = await authClient.PostAsJsonAsync("/auth/mfa/enable", enableRequest);
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        // 6. Login again (MFA Enabled now) -> should return 202 Accepted + ChallengeToken
        var mfaLoginMsg = new HttpRequestMessage(HttpMethod.Post, "/auth/token")
        {
            Content = JsonContent.Create(loginRequest)
        };
        mfaLoginMsg.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var mfaLoginResponse = await client.SendAsync(mfaLoginMsg);
        Assert.Equal(HttpStatusCode.Accepted, mfaLoginResponse.StatusCode);

        var challengeResult = await mfaLoginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(challengeResult);
        Assert.True(challengeResult.MfaRequired);
        Assert.NotNull(challengeResult.ChallengeToken);

        // 7. Verify MFA Challenge using ChallengeToken
        var verifyCode = CalculateTotp(enrollResult.Secret);
        var verifyChallengeRequest = new MfaLoginRequest
        {
            Code = verifyCode,
            ChallengeToken = challengeResult.ChallengeToken
        };

        var verifyResponse = await client.PostAsJsonAsync("/auth/mfa/verify", verifyChallengeRequest);
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var finalAuthResult = await verifyResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(finalAuthResult);
        Assert.NotNull(finalAuthResult.AccessToken);

        // 8. Logout / Revoke Token
        var finalAuthClient = _factory.CreateClient();
        finalAuthClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", finalAuthResult.AccessToken);

        var logoutResponse = await finalAuthClient.PostAsync("/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        // 9. Subsequent request using revoked token should be rejected (401 Unauthorized)
        var revokedResponse = await finalAuthClient.PostAsync("/auth/mfa/enroll", null);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);
    }

    private static string CalculateTotp(string secret)
    {
        // Simple internal TOTP calculation helper for testing
        // Decode base32
        var secretBytes = DecodeBase32(secret);
        var epoch = DateTimeOffset.UnixEpoch;
        var elapsed = DateTimeOffset.UtcNow - epoch;
        var step = (long)(elapsed.TotalSeconds / 30);

        var stepBytes = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(stepBytes);
        }

        var data = new byte[8];
        Array.Copy(stepBytes, 0, data, 8 - stepBytes.Length, stepBytes.Length);

        using var hmac = new System.Security.Cryptography.HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(data);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        var otp = binary % 1000000;
        return otp.ToString("D6");
    }

    private static byte[] DecodeBase32(string base32)
    {
        base32 = base32.Trim().ToUpperInvariant();
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
