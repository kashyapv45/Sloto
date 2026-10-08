using System.Collections.Generic;
using Dapper;
using MediatR;
using SaasEngine.Contracts.Identity;
using SaasEngine.Domain.Audit;
using SaasEngine.Domain.Billing;
using SaasEngine.Domain.Shared;
using SaasEngine.Api.Features.Billing;
using SaasEngine.Api.Infrastructure.Data;
using SaasEngine.Api.Infrastructure.Security;
using SaasEngine.Api.Infrastructure.Outbox;

namespace SaasEngine.Api.Features.Identity;

/// <summary>
/// Exception thrown when a user registration fails due to an email that is already registered.
/// </summary>
public sealed class DuplicateEmailException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    public DuplicateEmailException(string message) : base(message) { }
}

/// <summary>
/// Command to register a new user in a tenant.
/// Enforces seat limits prior to execution. Audited as a User registration action.
/// </summary>
public sealed record RegisterUserCommand(
    Guid TenantId,
    string Email,
    string Name,
    string Role,
    string Password) : IRequest<RegisterUserResponse>, ITenantAwareRequest, IPlanEnforcedRequest, IAuditableRequest
{
    /// <inheritdoc />
    public string AuditAction => "Register";

    /// <inheritdoc />
    public string AuditResourceType => "User";

    /// <inheritdoc />
    public Guid? ResourceId => null; // assigned after creation

    /// <inheritdoc />
    public IReadOnlyList<PlanEnforcementPolicy> PlanPolicies { get; } =
        [new PlanEnforcementPolicy(PlanPolicyType.Seats)];
}

/// <summary>Handler for RegisterUserCommand.</summary>
public sealed class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, RegisterUserResponse>
{
    private readonly IAdminConnectionFactory _adminDb;
    private readonly ITenantService _tenantService;
    private readonly OutboxService _outboxService;

    /// <summary>Initializes a new instance.</summary>
    public RegisterUserCommandHandler(
        IAdminConnectionFactory adminDb,
        ITenantService tenantService,
        OutboxService outboxService)
    {
        _adminDb = adminDb;
        _tenantService = tenantService;
        _outboxService = outboxService;
    }

    /// <inheritdoc />
    public async Task<RegisterUserResponse> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        using var connection = await _adminDb.CreateAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        // Check if user already exists
        var existingUser = await connection.QuerySingleOrDefaultAsync<string>(
            "SELECT email FROM users WHERE tenant_id = @TenantId AND email = @Email",
            new { TenantId = request.TenantId.ToString(), Email = request.Email.ToLowerInvariant() },
            transaction).ConfigureAwait(false);

        if (existingUser is not null)
        {
            throw new DuplicateEmailException("User with this email already exists in this tenant.");
        }

        // Prevent privilege escalation: only the first user of a tenant can initialize as admin via self-registration
        var userCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM users WHERE tenant_id = @TenantId",
            new { TenantId = request.TenantId.ToString() },
            transaction).ConfigureAwait(false);

        var assignedRole = request.Role.ToLowerInvariant();
        if (userCount > 0 && assignedRole == "admin")
        {
            assignedRole = "member";
        }

        var id = Guid.NewGuid();
        var hashedPassword = PasswordHasher.Hash(request.Password);
        var now = DateTimeOffset.UtcNow;

        await connection.ExecuteAsync(
            @"INSERT INTO users (id, tenant_id, email, name, role, password_hash, mfa_enabled, status, created_at)
              VALUES (@Id, @TenantId, @Email, @Name, @Role, @PasswordHash, false, 'active', @CreatedAt)",
            new
            {
                Id = id,
                TenantId = request.TenantId.ToString(),
                Email = request.Email.ToLowerInvariant(),
                request.Name,
                Role = assignedRole,
                PasswordHash = hashedPassword,
                CreatedAt = now
            },
            transaction).ConfigureAwait(false);

        // Fetch tenant tier to establish context
        var tenantTier = await connection.QuerySingleOrDefaultAsync<string>(
            "SELECT tier FROM tenants WHERE id = @TenantId",
            new { TenantId = request.TenantId.ToString() },
            transaction).ConfigureAwait(false) ?? "standard";

        _tenantService.SetTenantContext(request.TenantId, tenantTier);

        // Enqueue Outbox Event
        await _outboxService.EnqueueEventAsync(connection, transaction, new UserRegisteredEvent(
            id,
            request.Email,
            request.Name,
            request.Role,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow)).ConfigureAwait(false);

        transaction.Commit();

        return new RegisterUserResponse
        {
            Id = id,
            Email = request.Email,
            Name = request.Name,
            Role = request.Role
        };
    }
}
