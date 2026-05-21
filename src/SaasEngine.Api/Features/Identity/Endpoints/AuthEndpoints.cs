using System.Security.Claims;
using Dapper;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SaasEngine.Contracts.Identity;
using SaasEngine.Domain.Shared;
using SaasEngine.Api.Infrastructure.Security;
using SaasEngine.Api.Infrastructure.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace SaasEngine.Api.Features.Identity.Endpoints;

/// <summary>
/// Defines endpoints for authentication, registration, MFA management, and logout.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Maps authentication-related minimal API endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth");

        group.MapPost("/register", Register);
        group.MapPost("/token", Login);
        group.MapPost("/mfa/verify", MfaVerify);

        // Protected endpoints (require authentication)
        group.MapPost("/mfa/enroll", MfaEnroll).RequireAuthorization();
        group.MapPost("/mfa/enable", MfaEnable).RequireAuthorization();
        group.MapPost("/logout", Logout).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> Register(
        RegisterUserRequest request,
        [FromServices] IMediator mediator,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantIdStr) || 
            !Guid.TryParse(tenantIdStr, out var tenantId))
        {
            return TypedResults.Json(new ErrorResponse("X-Tenant-Id header is required."), AppJsonSerializerContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await mediator.Send(new RegisterUserCommand(
            tenantId,
            request.Email,
            request.Name,
            request.Role,
            request.Password
        ), cancellationToken).ConfigureAwait(false);

        return TypedResults.Json(response, AppJsonSerializerContext.Default.RegisterUserResponse, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Login(
        AuthTokenRequest request,
        [FromServices] IAdminConnectionFactory adminDb,
        [FromServices] JwtTokenGenerator jwtTokenGenerator,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantIdStr) || 
            !Guid.TryParse(tenantIdStr, out var tenantId))
        {
            return TypedResults.Json(new ErrorResponse("X-Tenant-Id header is required."), AppJsonSerializerContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
        }

        using var connection = await adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);

        // Fetch user and join tenant to get their tier
        var user = await connection.QuerySingleOrDefaultAsync<UserRecord>(
            @"SELECT u.id, u.tenant_id, u.email, u.name, u.role, u.password_hash, u.mfa_secret, u.mfa_enabled, u.status, u.created_at, t.tier
              FROM users u
              INNER JOIN tenants t ON u.tenant_id = t.id
              WHERE u.tenant_id = @TenantId AND u.email = @Email AND u.status = 'active'",
            new { TenantId = tenantId, Email = request.Email.ToLowerInvariant() }).ConfigureAwait(false);

        if (user is null || !PasswordHasher.Verify(request.Password, user.Password_hash))
        {
            return TypedResults.Unauthorized();
        }

        if (user.Mfa_enabled)
        {
            // If code is provided in login request, verify immediately
            if (!string.IsNullOrWhiteSpace(request.MfaCode))
            {
                if (!TotpService.VerifyCode(user.Mfa_secret ?? "", request.MfaCode))
                {
                    return TypedResults.Json(new ErrorResponse("Invalid MFA code."), AppJsonSerializerContext.Default.ErrorResponse, statusCode: StatusCodes.Status401Unauthorized);
                }
                
                var token = jwtTokenGenerator.GenerateToken(user.Id, user.Tenant_id, user.Tier, user.Role);
                var tokenResponse = new AuthResponse
                {
                    AccessToken = token,
                    ExpiresIn = 3600
                };
                return TypedResults.Json(tokenResponse, AppJsonSerializerContext.Default.AuthResponse);
            }

            // Issue temporary challenge token
            var challengeToken = jwtTokenGenerator.GenerateToken(
                user.Id, 
                user.Tenant_id, 
                user.Tier, 
                user.Role, 
                expirationMinutes: 5, 
                mfaPending: true);

            var challengeResponse = new AuthResponse
            {
                MfaRequired = true,
                ChallengeToken = challengeToken
            };

            return TypedResults.Json(challengeResponse, AppJsonSerializerContext.Default.AuthResponse, statusCode: StatusCodes.Status202Accepted);
        }

        // Standard token
        var jwt = jwtTokenGenerator.GenerateToken(user.Id, user.Tenant_id, user.Tier, user.Role);
        var finalResponse = new AuthResponse
        {
            AccessToken = jwt,
            ExpiresIn = 3600
        };
        return TypedResults.Json(finalResponse, AppJsonSerializerContext.Default.AuthResponse);
    }

    private static async Task<IResult> MfaVerify(
        MfaLoginRequest request,
        [FromServices] IAdminConnectionFactory adminDb,
        [FromServices] JwtTokenGenerator jwtTokenGenerator,
        CancellationToken cancellationToken)
    {
        var tokenHandler = new JsonWebTokenHandler();
        
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtTokenGenerator.GetPublicKey(),
            ValidateIssuer = true,
            ValidIssuer = "SaasEngine",
            ValidateAudience = true,
            ValidAudience = "SaasEngine.Api",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var result = await tokenHandler.ValidateTokenAsync(request.ChallengeToken, validationParameters).ConfigureAwait(false);
        if (!result.IsValid)
        {
            return TypedResults.Unauthorized();
        }

        var claims = result.ClaimsIdentity;
        var mfaPending = claims.FindFirst("mfa_pending")?.Value;
        if (mfaPending != "true")
        {
            return TypedResults.Json(new ErrorResponse("Invalid challenge token."), AppJsonSerializerContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
        }

        var sub = claims.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var tid = claims.FindFirst("tid")?.Value;
        if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(tid))
        {
            return TypedResults.Json(new ErrorResponse("Invalid challenge token claims."), AppJsonSerializerContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
        }

        using var connection = await adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);

        var user = await connection.QuerySingleOrDefaultAsync<UserRecord>(
            @"SELECT u.id, u.tenant_id, u.email, u.name, u.role, u.password_hash, u.mfa_secret, u.mfa_enabled, u.status, u.created_at, t.tier
              FROM users u
              INNER JOIN tenants t ON u.tenant_id = t.id
              WHERE u.id = @Id AND u.tenant_id = @TenantId AND u.status = 'active'",
            new { Id = Guid.Parse(sub), TenantId = Guid.Parse(tid) }).ConfigureAwait(false);

        if (user is null || !TotpService.VerifyCode(user.Mfa_secret ?? "", request.Code))
        {
            return TypedResults.Json(new ErrorResponse("Invalid MFA code."), AppJsonSerializerContext.Default.ErrorResponse, statusCode: StatusCodes.Status401Unauthorized);
        }

        var token = jwtTokenGenerator.GenerateToken(user.Id, user.Tenant_id, user.Tier, user.Role);
        var tokenResponse = new AuthResponse
        {
            AccessToken = token,
            ExpiresIn = 3600
        };
        return TypedResults.Json(tokenResponse, AppJsonSerializerContext.Default.AuthResponse);
    }

    private static async Task<IResult> MfaEnroll(
        [FromServices] IAdminConnectionFactory adminDb,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
            ?? context.User.FindFirst("sub")?.Value;
        var tenantIdClaim = context.User.FindFirst("tid")?.Value;
        var emailClaim = context.User.FindFirst(ClaimTypes.Name)?.Value 
            ?? context.User.FindFirst("email")?.Value 
            ?? "user@tenant";

        if (string.IsNullOrEmpty(userIdClaim) || string.IsNullOrEmpty(tenantIdClaim))
        {
            return TypedResults.Unauthorized();
        }

        var userId = Guid.Parse(userIdClaim);
        var tenantId = Guid.Parse(tenantIdClaim);

        var secret = TotpService.GenerateSecret();
        var qrCodeUri = TotpService.GenerateQrCodeUri(emailClaim, secret);

        using var connection = await adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
            "UPDATE users SET mfa_secret = @Secret, mfa_enabled = false WHERE id = @UserId AND tenant_id = @TenantId",
            new { Secret = secret, UserId = userId, TenantId = tenantId }).ConfigureAwait(false);

        var response = new MfaEnrollResponse
        {
            Secret = secret,
            QrCodeDataUrl = qrCodeUri
        };

        return TypedResults.Json(response, AppJsonSerializerContext.Default.MfaEnrollResponse);
    }

    private static async Task<IResult> MfaEnable(
        MfaVerifyRequest request,
        [FromServices] IAdminConnectionFactory adminDb,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
            ?? context.User.FindFirst("sub")?.Value;
        var tenantIdClaim = context.User.FindFirst("tid")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || string.IsNullOrEmpty(tenantIdClaim))
        {
            return TypedResults.Unauthorized();
        }

        var userId = Guid.Parse(userIdClaim);
        var tenantId = Guid.Parse(tenantIdClaim);

        using var connection = await adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);

        var user = await connection.QuerySingleOrDefaultAsync<UserRecord>(
            @"SELECT id, tenant_id, mfa_secret, mfa_enabled
              FROM users
              WHERE id = @Id AND tenant_id = @TenantId AND status = 'active'",
            new { Id = userId, TenantId = tenantId }).ConfigureAwait(false);

        if (user is null || string.IsNullOrEmpty(user.Mfa_secret))
        {
            return TypedResults.Json(new ErrorResponse("MFA has not been enrolled for this user."), AppJsonSerializerContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TotpService.VerifyCode(user.Mfa_secret, request.Code))
        {
            return TypedResults.Json(new ErrorResponse("Invalid verification code."), AppJsonSerializerContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
        }

        await connection.ExecuteAsync(
            "UPDATE users SET mfa_enabled = true WHERE id = @UserId AND tenant_id = @TenantId",
            new { UserId = userId, TenantId = tenantId }).ConfigureAwait(false);

        return TypedResults.Json(new MessageResponse("MFA enabled successfully."), AppJsonSerializerContext.Default.MessageResponse);
    }

    private static async Task<IResult> Logout(
        [FromServices] TokenBlacklistService blacklistService,
        HttpContext context)
    {
        var jti = context.User.FindFirst("jti")?.Value 
            ?? context.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        
        var expClaim = context.User.FindFirst("exp")?.Value 
            ?? context.User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;

        if (string.IsNullOrEmpty(jti))
        {
            return TypedResults.Json(new ErrorResponse("Invalid token claims."), AppJsonSerializerContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
        }

        TimeSpan remainingLifetime = TimeSpan.FromHours(1);
        if (!string.IsNullOrEmpty(expClaim) && long.TryParse(expClaim, out var expUnix))
        {
            var expTime = DateTimeOffset.FromUnixTimeSeconds(expUnix);
            var diff = expTime - DateTimeOffset.UtcNow;
            if (diff > TimeSpan.Zero)
            {
                remainingLifetime = diff;
            }
        }

        await blacklistService.BlacklistTokenAsync(jti, remainingLifetime).ConfigureAwait(false);

        return TypedResults.Json(new MessageResponse("Logged out successfully."), AppJsonSerializerContext.Default.MessageResponse);
    }

    private sealed record UserRecord
    {
        public required Guid Id { get; init; }
        public required Guid Tenant_id { get; init; }
        public required string Email { get; init; }
        public required string Name { get; init; }
        public required string Role { get; init; }
        public required string Password_hash { get; init; }
        public string? Mfa_secret { get; init; }
        public required bool Mfa_enabled { get; init; }
        public required string Status { get; init; }
        public required DateTimeOffset Created_at { get; init; }
        public required string Tier { get; init; }
    }
}
