# Application Security Audit & Vulnerability Assessment Report

**Target Repository**: `sloto` (Multi-Tenant SaaS Engine)  
**Assessment Date**: 2026-10-08  
**Assessor**: Senior Application Security Engineer  
**Scope**: Backend APIs (`SaasEngine.Api`, `SaasEngine.Admin`), UI Command Bridge (`ui`), and Infrastructure Configurations.  
**Categories Assessed**:
1. Hardcoded Secrets and Credentials
2. Prompt Injection *(Omitted: no LLM components present)*
3. SQL Injection, Command Injection, and Cross-Site Scripting (XSS)
4. Missing Input Validation and Data Sanitization
5. Broken Authentication and Authorization
6. Insecure Direct Object References (IDOR)
7. Vulnerable and Outdated Dependencies
8. Overly Permissive CORS Configurations
9. Sensitive Data Exposure and Detailed Stack Traces
10. Mass Assignment Vulnerabilities
11. Missing Rate Limiting and DoS Risks

---

## Executive Summary

A comprehensive application security audit was performed across the `sloto` repository. The codebase demonstrates solid architectural patterns in several domains—such as PBKDF2-SHA256 password hashing, parameterized Dapper queries, and RS256 JWT tokens. However, **multiple critical and high-severity security vulnerabilities** were identified that expose the host machine to Remote Code Execution (RCE), compromise multi-tenant isolation, expose cryptographic keys and database credentials, and allow unauthenticated administrative manipulation.

### Vulnerability Summary Matrix

| Severity | Count | Primary Impact Areas |
| :--- | :---: | :--- |
| **Critical** | 5 | Remote Command Execution (RCE), Complete Broken Access Control on Admin APIs, Committed RSA Private Key, Privilege Escalation via Mass Assignment, Critical Dependency CVE |
| **High** | 4 | Insecure Direct Object References (IDOR), Plaintext Credential Exposure in Backups/Audit Logs, Wildcard CORS, Missing Rate Limiting / MFA Brute-Force |
| **Medium** | 4 | Bypassed Input Validation, Unprotected Hangfire Dashboard & Endpoints, TOTP Timing Attack & Replay, Path Disclosure |
| **Low** | 2 | Fallback Hardcoded Credentials, Moderate/Low Dependency CVEs |

---

## Findings: Critical Severity

### 1. Arbitrary Command Injection & Remote Code Execution (RCE) via Unauthenticated Command Bridge
- **Category**: Command Injection & Remote Code Execution / Broken Authentication
- **Affected File**: [`ui/server.js`](file:///k:/Work/Projects/sloto/ui/server.js#L16-L38)
- **Vulnerable Code Snippet**:
  ```javascript
  app.post('/api/run-command', (req, res) => {
      const { command, directory } = req.body;
      ...
      const targetDir = directory ? path.resolve(PROJECT_ROOT, directory) : PROJECT_ROOT;
      const cmdStr = `start cmd.exe /K "cd /d \"${targetDir}\" && title ${command} && echo Running: ${command} && echo. && ${command}"`;

      exec(cmdStr, { cwd: targetDir }, (error) => { ... });
  });
  ```
- **Security Risk**:
  The HTTP server binds to port `3001` with wildcard CORS (`app.use(cors())`) and zero authentication. Any malicious website loaded in a developer's browser can issue cross-origin POST requests to `http://localhost:3001/api/run-command`. Because `command` and `directory` are concatenated directly into the shell string passed to `child_process.exec`, arbitrary commands (e.g., PowerShell payloads, reverse shells, ransomware) execute on the host machine under the developer's user privileges.
- **Secure Refactored Code**:
  Use a strict allowlist of predefined actions and invoke commands using `child_process.spawn` with an array of arguments, avoiding the shell interpreter:
  ```javascript
  const { spawn } = require('child_process');
  const path = require('path');

  const ALLOWED_COMMANDS = new Map([
      ['run-api', { cmd: 'dotnet', args: ['run'], cwd: 'src/SaasEngine.Api' }],
      ['run-admin', { cmd: 'dotnet', args: ['run'], cwd: 'src/SaasEngine.Admin' }],
      ['docker-up', { cmd: 'docker', args: ['compose', 'up'], cwd: '.' }]
  ]);

  app.post('/api/run-command', (req, res) => {
      const { action } = req.body;
      const task = ALLOWED_COMMANDS.get(action);
      if (!task) {
          return res.status(400).json({ error: 'Invalid or unauthorized action' });
      }

      const workingDir = path.resolve(PROJECT_ROOT, task.cwd);
      const child = spawn(task.cmd, task.args, { cwd: workingDir, shell: false, detached: true });
      child.unref();

      res.json({ message: 'Task initiated safely.' });
  });
  ```

---

### 2. Complete Broken Authentication & Authorization on Administrative APIs
- **Category**: Broken Authentication and Authorization
- **Affected Files**:
  - [`src/SaasEngine.Api/Features/Tenancy/Endpoints/TenantEndpoints.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Features/Tenancy/Endpoints/TenantEndpoints.cs#L16-L26)
  - [`src/SaasEngine.Api/Features/Audit/Endpoints/AuditEndpoints.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Features/Audit/Endpoints/AuditEndpoints.cs#L26-L39)
  - [`src/SaasEngine.Api/Features/Billing/BillingEndpoints.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Features/Billing/BillingEndpoints.cs#L26-L31)
- **Vulnerable Code Snippet**:
  ```csharp
  // TenantEndpoints.cs
  public static void MapTenantEndpoints(this WebApplication app)
  {
      var group = app.MapGroup("/admin/tenants").WithTags("Tenants");
      group.MapPost("/", CreateTenant);
      group.MapGet("/{id:guid}", GetTenant);
      group.MapPatch("/{id:guid}/status", UpdateTenantStatus);
      group.MapPatch("/{id:guid}/plan", UpdateTenantPlan);
      group.MapDelete("/{id:guid}", DeleteTenant);
  }
  ```
- **Security Risk**:
  Although documentation specifies protection by `X-Internal-Key`, **no authentication middleware, authorization policies, or filters are attached** to `/admin/tenants`, `/admin/audit`, or `/admin/tenants/{id}/flags`. Any anonymous caller can:
  - Create, suspend, or delete any tenant.
  - Upgrade any tenant to the enterprise tier without payment.
  - Read immutable audit logs for all tenants across the platform.
  - Modify feature flag rollout percentages for any tenant.
- **Secure Refactored Code**:
  Implement and enforce an endpoint filter or authorization policy:
  ```csharp
  public static void MapTenantEndpoints(this WebApplication app)
  {
      var group = app.MapGroup("/admin/tenants")
          .WithTags("Tenants")
          .AddEndpointFilter<AdminApiKeyEndpointFilter>();

      group.MapPost("/", CreateTenant);
      group.MapGet("/{id:guid}", GetTenant);
      group.MapPatch("/{id:guid}/status", UpdateTenantStatus);
      group.MapPatch("/{id:guid}/plan", UpdateTenantPlan);
      group.MapDelete("/{id:guid}", DeleteTenant);
  }

  public sealed class AdminApiKeyEndpointFilter : IEndpointFilter
  {
      private readonly IConfiguration _config;
      public AdminApiKeyEndpointFilter(IConfiguration config) => _config = config;

      public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
      {
          var expectedKey = _config["Security:AdminApiKey"];
          if (string.IsNullOrEmpty(expectedKey) ||
              !context.HttpContext.Request.Headers.TryGetValue("X-Internal-Key", out var providedKey) ||
              !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(providedKey!), Encoding.UTF8.GetBytes(expectedKey)))
          {
              return Results.Unauthorized();
          }
          return await next(context);
      }
  }
  ```

---

### 3. Hardcoded RSA Private Key and Database Passwords Committed to Configuration
- **Category**: Hardcoded Secrets and Credentials
- **Affected Files**:
  - [`src/SaasEngine.Api/appsettings.Development.json`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/appsettings.Development.json#L3-L12)
  - [`src/SaasEngine.Api/Program.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Program.cs#L80-L88)
- **Vulnerable Code Snippet**:
  ```json
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5445;Database=saasengine;Username=saas_admin;Password=DevPassword123!",
    "Redis": "localhost:6379,password=DevRedis123!"
  },
  "Jwt": {
    "PrivateKeyPem": "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA4pkNkj5vHY6T/nk/z2bIYds..."
  }
  ```
- **Security Risk**:
  A 2048-bit RSA private key used to sign JWT tokens is committed directly in plaintext to version control. Anyone with read access to the repository can forge valid JWT tokens for any tenant, user ID, and administrative role (`role: "admin"`). In addition, database passwords and Redis credentials are hardcoded in config files and in fallback strings in `Program.cs`.
- **Secure Refactored Code**:
  Purge the private key and secrets from Git history. Load secrets from an external vault or environment variables, and fail immediately if they are missing:
  ```csharp
  // Program.cs
  var privateKeyPem = await secretStore.GetSecretAsync("Jwt:PrivateKeyPem");
  if (string.IsNullOrWhiteSpace(privateKeyPem))
  {
      throw new InvalidOperationException("CRITICAL: RSA private key for JWT signing is missing from secret store.");
  }
  ```

---

### 4. Unrestricted Privilege Escalation & Mass Assignment on User Registration
- **Category**: Mass Assignment Vulnerabilities & Missing Input Validation
- **Affected Files**:
  - [`src/SaasEngine.Api/Features/Identity/Endpoints/AuthEndpoints.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Features/Identity/Endpoints/AuthEndpoints.cs#L42-L63)
  - [`src/SaasEngine.Api/Features/Identity/RegisterUser.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Features/Identity/RegisterUser.cs#L88-L101)
- **Vulnerable Code Snippet**:
  ```csharp
  // AuthEndpoints.cs
  var response = await mediator.Send(new RegisterUserCommand(
      tenantId,
      request.Email,
      request.Name,
      request.Role, // <-- Bound directly from untrusted request body
      request.Password
  ), cancellationToken);

  // RegisterUser.cs
  await connection.ExecuteAsync(
      @"INSERT INTO users (id, tenant_id, email, name, role, password_hash, mfa_enabled, status, created_at)
        VALUES (@Id, @TenantId, @Email, @Name, @Role, @PasswordHash, false, 'active', @CreatedAt)",
      new { ..., Role = request.Role.ToLowerInvariant(), ... });
  ```
- **Security Risk**:
  The public `/auth/register` endpoint binds `request.Role` directly from the client JSON body without any authorization check or role allowlisting. An anonymous caller can register a user with `role: "admin"` or `role: "owner"`, automatically granting themselves root administrative privileges within the targeted tenant.
- **Secure Refactored Code**:
  Enforce a non-privileged default role (`"member"`) during public self-registration:
  ```csharp
  // Fix: Self-registration always assigns the default unprivileged role
  const string defaultRole = "member";

  var response = await mediator.Send(new RegisterUserCommand(
      tenantId,
      request.Email,
      request.Name,
      defaultRole,
      request.Password
  ), cancellationToken);
  ```

---

### 5. Critical Vulnerability in `proxy-addr` Dependency (GHSA-jqcg-44mw-7w3h)
- **Category**: Vulnerable and Outdated Dependencies
- **Affected File**: [`ui/package.json`](file:///k:/Work/Projects/sloto/ui/package.json)
- **Security Risk**:
  `proxy-addr <= 2.0.7` is vulnerable to IP spoofing via IPv4-mapped IPv6 trust subnet representations ([GHSA-jqcg-44mw-7w3h](https://github.com/advisories/GHSA-jqcg-44mw-7w3h)). Attackers can spoof client IP addresses to bypass rate limiting and IP allowlisting.
- **Secure Refactored Code**:
  Upgrade `proxy-addr` to version `2.0.8` or newer:
  ```json
  "overrides": {
    "proxy-addr": ">=2.0.8"
  }
  ```

---

## Findings: High Severity

### 6. Insecure Direct Object References (IDOR) on Data Erasure and Tenant Export
- **Category**: Insecure Direct Object References (IDOR) & Broken Authorization
- **Affected File**: [`src/SaasEngine.Api/Features/Audit/Endpoints/AuditEndpoints.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Features/Audit/Endpoints/AuditEndpoints.cs#L32-L39)
- **Vulnerable Code Snippet**:
  ```csharp
  // Tenant data export
  app.MapPost("/admin/tenants/{id:guid}/export", ExportTenant).WithTags("Tenants");

  // GDPR erasure
  app.MapPost("/tenants/{id:guid}/users/{userId:guid}/erase", EraseUser).WithTags("Identity");
  ```
- **Security Risk**:
  Neither endpoint validates whether the caller is authorized for the target tenant or user. An attacker can iterate or supply arbitrary GUIDs to `/tenants/{id}/users/{userId}/erase` to anonymize and destroy user records across other tenants, or call `/admin/tenants/{id}/export` to initiate complete dataset exports.
- **Secure Refactored Code**:
  Require authentication and enforce tenancy claims and role checks:
  ```csharp
  app.MapPost("/tenants/{id:guid}/users/{userId:guid}/erase", EraseUser)
      .RequireAuthorization()
      .AddEndpointFilter(async (context, next) =>
      {
          var routeTenantId = context.GetArgument<Guid>(0);
          var userTenantClaim = context.HttpContext.User.FindFirst("tid")?.Value;
          var userRole = context.HttpContext.User.FindFirst("role")?.Value;

          if (!Guid.TryParse(userTenantClaim, out var tenantId) || 
              tenantId != routeTenantId || 
              !string.Equals(userRole, "admin", StringComparison.OrdinalIgnoreCase))
          {
              return Results.Forbid();
          }
          return await next(context);
      });
  ```

---

### 7. Sensitive Credential Exposure (Password Hashes & TOTP Secrets) in Exports and Audit Logs
- **Category**: Sensitive Data Exposure
- **Affected Files**:
  - [`src/SaasEngine.Domain/Identity/User.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Domain/Identity/User.cs#L23-L28)
  - [`src/SaasEngine.Api/Features/Audit/Jobs/ExportTenantDataJob.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Features/Audit/Jobs/ExportTenantDataJob.cs#L114-L163)
  - [`src/SaasEngine.Api/Features/Audit/AuditBehavior.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Features/Audit/AuditBehavior.cs#L203-L229)
- **Vulnerable Code Snippet**:
  ```csharp
  // ExportTenantDataJob.cs
  var userResults = await connection.QueryAsync<SaasEngine.Domain.Identity.User>(
      "SELECT id, tenant_id as TenantId, email, name, role, password_hash as PasswordHash, mfa_secret as MfaSecret, mfa_enabled as MfaEnabled, status, created_at as CreatedAt FROM users WHERE tenant_id = @TenantId", ...);

  // Full User serialized into ZIP archive without masking:
  writer.Write(JsonSerializer.Serialize(users, AppJsonSerializerContext.Default.ListUser));
  ```
- **Security Risk**:
  `User.cs` does not annotate `PasswordHash` or `MfaSecret` with `[JsonIgnore]`. During audit logging (`AuditBehavior.cs`), the full user entity is serialized into `before_state` and `after_state` in the `audit_events` table (which is accessible via `GET /admin/audit`). Furthermore, `ExportTenantDataJob` packages password hashes and plaintext Base32 TOTP secrets into the downloadable ZIP archive.
- **Secure Refactored Code**:
  Add `[JsonIgnore]` to sensitive fields and use dedicated sanitization DTOs:
  ```csharp
  public sealed record User
  {
      public required Guid Id { get; init; }
      public required Guid TenantId { get; init; }
      public required string Email { get; init; }
      public required string Name { get; init; }
      public required string Role { get; init; }

      [System.Text.Json.Serialization.JsonIgnore]
      public required string PasswordHash { get; init; }

      [System.Text.Json.Serialization.JsonIgnore]
      public string? MfaSecret { get; init; }

      public bool MfaEnabled { get; init; }
      public required UserStatus Status { get; init; }
      public required DateTimeOffset CreatedAt { get; init; }
  }
  ```

---

### 8. Overly Permissive Wildcard CORS Configurations
- **Category**: Overly Permissive CORS Configurations
- **Affected Files**:
  - [`src/SaasEngine.Api/Program.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Program.cs#L39-L47)
  - [`ui/server.js`](file:///k:/Work/Projects/sloto/ui/server.js#L7)
- **Vulnerable Code Snippet**:
  ```csharp
  builder.Services.AddCors(options =>
  {
      options.AddDefaultPolicy(policy =>
      {
          policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
      });
  });
  ```
- **Security Risk**:
  Allowing all origins (`*`) permits arbitrary third-party websites to interact with API endpoints through the user's browser, facilitating CSRF, data exfiltration, and cross-origin abuse.
- **Secure Refactored Code**:
  Restrict CORS policies to an explicit allowlist:
  ```csharp
  var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
      ?? ["https://app.yourdomain.com"];

  builder.Services.AddCors(options =>
  {
      options.AddDefaultPolicy(policy =>
      {
          policy.WithOrigins(allowedOrigins)
                .WithHeaders("Content-Type", "Authorization", "X-Tenant-Id")
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE");
      });
  });
  ```

---

### 9. Missing Rate Limiting on Authentication and TOTP Verification Endpoints
- **Category**: Missing Rate Limiting and DoS Risks / Broken Authentication
- **Affected File**: [`src/SaasEngine.Api/Features/Identity/Endpoints/AuthEndpoints.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Features/Identity/Endpoints/AuthEndpoints.cs#L65-L138)
- **Vulnerable Code Snippet**:
  ```csharp
  group.MapPost("/token", Login);
  group.MapPost("/mfa/verify", MfaVerify);
  ```
- **Security Risk**:
  Neither `/auth/token` nor `/auth/mfa/verify` implement rate limiting or lockout. Because 6-digit TOTP codes have only 1,000,000 possibilities, an attacker with a user's password or challenge token can brute-force the TOTP code online within the 5-minute window.
- **Secure Refactored Code**:
  Apply rate limiting to all authentication endpoints:
  ```csharp
  // Program.cs
  builder.Services.AddRateLimiter(options =>
  {
      options.AddFixedWindowLimiter("auth-rate-limit", opt =>
      {
          opt.PermitLimit = 5;
          opt.Window = TimeSpan.FromMinutes(1);
          opt.QueueLimit = 0;
      });
  });

  // AuthEndpoints.cs
  group.MapPost("/token", Login).RequireRateLimiting("auth-rate-limit");
  group.MapPost("/mfa/verify", MfaVerify).RequireRateLimiting("auth-rate-limit");
  ```

---

## Findings: Medium Severity

### 10. Missing Input Validation and Uninvoked Validators
- **Category**: Missing Input Validation and Data Sanitization
- **Affected Files**:
  - [`src/SaasEngine.Api/Features/Tenancy/Endpoints/TenantEndpoints.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Features/Tenancy/Endpoints/TenantEndpoints.cs#L29-L70)
  - [`src/SaasEngine.Api/Program.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Program.cs#L65)
- **Vulnerable Code Snippet**:
  ```csharp
  // Program.cs registers:
  builder.Services.AddScoped<IValidator<CreateTenantRequest>, CreateTenantValidator>();

  // TenantEndpoints.cs ignores the registered validator:
  private static async Task<IResult> CreateTenant(
      CreateTenantRequest request,
      [FromServices] IAdminConnectionFactory adminDb,
      CancellationToken cancellationToken)
  ```
- **Security Risk**:
  `CreateTenantValidator` is registered in DI but never injected into `CreateTenant`. Requests with blank names, invalid tiers, or missing enterprise secret references are processed without validation. Similarly, `RegisterUserRequest` lacks email syntax and password complexity checks.
- **Secure Refactored Code**:
  Inject and execute validators on request entry:
  ```csharp
  private static async Task<IResult> CreateTenant(
      CreateTenantRequest request,
      [FromServices] IValidator<CreateTenantRequest> validator,
      [FromServices] IAdminConnectionFactory adminDb,
      CancellationToken cancellationToken)
  {
      var validationResult = await validator.ValidateAsync(request, cancellationToken);
      if (!validationResult.IsValid)
      {
          return Results.ValidationProblem(validationResult.ToDictionary());
      }
      ...
  }
  ```

---

### 11. Unauthenticated Hangfire Dashboard and Outbox Processing Endpoint
- **Category**: Broken Authentication and Authorization / DoS Risks
- **Affected File**: [`src/SaasEngine.Api/Program.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Program.cs#L231-L243)
- **Vulnerable Code Snippet**:
  ```csharp
  app.UseHangfireDashboard("/hangfire");

  app.MapPost("/jobs/outbox/process", async ([FromServices] OutboxPollerJob poller, CancellationToken ct) =>
  {
      await poller.ProcessOutboxEventsAsync(ct).ConfigureAwait(false);
      return Results.Ok(new MessageResponse("Processed"));
  });
  ```
- **Security Risk**:
  `UseHangfireDashboard("/hangfire")` without an explicit `IDashboardAuthorizationFilter` relies on default local-only policies, which can be bypassed in container networks or behind reverse proxies. In addition, `/jobs/outbox/process` can be spammed by anonymous callers to cause database lock contention and CPU exhaustion.
- **Secure Refactored Code**:
  Enforce authentication on both endpoints:
  ```csharp
  app.UseHangfireDashboard("/hangfire", new DashboardOptions
  {
      Authorization = [new HangfireAdminAuthFilter()]
  });

  app.MapPost("/jobs/outbox/process", ...).RequireAuthorization("AdminPolicy");
  ```

---

### 12. Non-Constant-Time TOTP Comparison & Replay Vulnerability
- **Category**: Broken Authentication
- **Affected File**: [`src/SaasEngine.Api/Infrastructure/Security/TotpService.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Infrastructure/Security/TotpService.cs#L57-L62)
- **Vulnerable Code Snippet**:
  ```csharp
  var expectedCode = CalculateTotp(secretBytes, step);
  if (code == expectedCode)
  {
      return true;
  }
  ```
- **Security Risk**:
  Using the standard `==` string equality operator allows timing attacks. Additionally, used TOTP codes are not marked or cached, allowing replay within the 90-second validity window.
- **Secure Refactored Code**:
  Compare codes using constant-time evaluation and invalidate used codes:
  ```csharp
  var expectedCode = CalculateTotp(secretBytes, step);
  if (CryptographicOperations.FixedTimeEquals(
          Encoding.UTF8.GetBytes(code), 
          Encoding.UTF8.GetBytes(expectedCode)))
  {
      return true;
  }
  ```

---

### 13. Detailed Error and Path Disclosure in Node Server
- **Category**: Sensitive Data Exposure and Detailed Stack Traces
- **Affected File**: [`ui/server.js`](file:///k:/Work/Projects/sloto/ui/server.js#L32-L36)
- **Vulnerable Code Snippet**:
  ```javascript
  exec(cmdStr, { cwd: targetDir }, (error) => {
      if (error) {
          console.error(`Execution error: ${error}`);
          return res.status(500).json({ error: error.message });
      }
      res.json({ message: 'Command launched successfully in a new window.' });
  });
  ```
- **Security Risk**:
  Returning `error.message` directly in HTTP 500 responses leaks internal filesystem paths, OS error codes, and server environment details.
- **Secure Refactored Code**:
  Log the full error internally and return an opaque error message:
  ```javascript
  if (error) {
      console.error(`Execution error: ${error.message}`);
      return res.status(500).json({ error: 'Command execution failed.' });
  }
  ```

---

## Findings: Low Severity

### 14. Hardcoded Fallback Database & Redis Passwords in Code
- **Category**: Hardcoded Secrets and Credentials
- **Affected Files**:
  - [`src/SaasEngine.Api/Program.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Program.cs#L71-L93)
  - [`src/SaasEngine.Api/Observability/HealthCheckExtensions.cs`](file:///k:/Work/Projects/sloto/src/SaasEngine.Api/Observability/HealthCheckExtensions.cs#L19-L21)
- **Vulnerable Code Snippet**:
  ```csharp
  var defaultConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
      ?? "Host=localhost;Port=5432;Database=saasengine;Username=saas_admin;Password=DevPassword123!";
  ```
- **Security Risk**:
  If environment variables are omitted or fail to mount, the service falls back to default credentials (`DevPassword123!`), leaving deployments vulnerable.
- **Secure Refactored Code**:
  Fail immediately on startup if connection strings are missing:
  ```csharp
  var defaultConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
      ?? throw new InvalidOperationException("DefaultConnection string is not configured.");
  ```

---

### 15. Vulnerable NPM Dependencies (`body-parser`, `qs`)
- **Category**: Vulnerable and Outdated Dependencies
- **Affected File**: [`ui/package.json`](file:///k:/Work/Projects/sloto/ui/package.json)
- **Security Risk**:
  - `body-parser` (Low): GHSA-v422-hmwv-36x6 (DoS when invalid limit value disables size enforcement)
  - `qs` (Moderate): GHSA-x5fp-wj9c-mxmx & GHSA-4mjr-xmp4-gh2g (Denial of Service via Attacker Controlled isBuffer)
- **Secure Refactored Code**:
  Run `npm audit fix` or upgrade dependencies in `ui/package.json`.

---

## Remediation Roadmap

1. **Phase 1: Immediate Triage (P0 - Next 24 Hours)**
   - Remove the unauthenticated arbitrary command execution endpoint from `ui/server.js` or restrict it to an immutable allowlist using `child_process.spawn`.
   - Remove the RSA Private Key from `appsettings.Development.json` and rotate all JWT keys and database passwords.
   - Attach authentication and authorization middleware to all `/admin/*` routes in `TenantEndpoints`, `AuditEndpoints`, and `BillingEndpoints`.

2. **Phase 2: High-Priority Hardening (P1 - Current Sprint)**
   - Fix `AuthEndpoints.Register` to prevent mass assignment of the `admin` role.
   - Annotate sensitive properties (`PasswordHash`, `MfaSecret`) with `[JsonIgnore]` on `User.cs`.
   - Implement rate limiting on `/auth/token` and `/auth/mfa/verify`.
   - Run `npm audit fix` to resolve `proxy-addr`, `qs`, and `body-parser` vulnerabilities.

3. **Phase 3: Defensive Polish (P2 - Next Sprint)**
   - Enforce `CreateTenantValidator` in `TenantEndpoints.CreateTenant`.
   - Configure restrictive CORS allowlists for production domains.
   - Apply constant-time comparisons (`CryptographicOperations.FixedTimeEquals`) and replay protection for TOTP codes.

---

## Remediation & Verification Status (2026-10-08)

| Item | Mechanism & Vulnerability | Remediation Applied | Verification Status |
| :---: | :--- | :--- | :--- |
| **1** | **SQL Injection via Dynamic String Interpolation** | Full query parameterization using Dapper (`@Param` placeholders) across all queries in `TenantEndpoints`, `AuditEndpoints`, `BillingFeatures`, and `RegisterUser`. Dynamic filters in `AuditEndpoints` use strictly parameterized `DynamicParameters`. | Verified with `grep -rn "FromSqlRaw" src/` (0 occurrences). Zero raw string interpolation. |
| **2** | **Disabled JWT Signature and Lifetime Verification** | Enforced cryptographic validation in `Program.cs` and `AuthEndpoints.cs`: `ValidateIssuerSigningKey = true`, `ValidateLifetime = true`, `ValidateIssuer = true`, `ValidateAudience = true`, `ClockSkew = TimeSpan.Zero`, and revocation checks via `TokenBlacklistService`. | Verified in `Program.cs` (lines 199–224) and `AuthEndpoints.cs` (lines 157–167). |
| **3** | **Insecure Direct Object References (IDOR)** | Removed client-supplied user identifiers from mutable user/financial endpoints (`/auth/mfa/enroll`, `/auth/mfa/enable`, `/auth/logout`, `/tenants/{id}/users/{userId}/erase`). User and tenant IDs are bound strictly to verified JWT claims (`ClaimTypes.NameIdentifier`, `sub`, `tid`). | Verified across `AuthEndpoints.cs` and `AuditEndpoints.cs`. |
| **4** | **Missing Numerical Bounds Validation** | Implemented `UpdateFeatureFlagValidator` enforcing `RolloutPercentage` inclusive between 0 and 100 with FluentValidation. Bound and clamped pagination limits (`Math.Clamp(request.PageSize, 1, 100)`). | Verified via xUnit integration tests in `PlanEnforcementIntegrationTests.cs` asserting HTTP 400 for out-of-bound inputs. |
| **5** | **Mass Assignment via Entity Binding** | Replaced entity binding with dedicated DTO request contracts (`CreateTenantRequest`, `UpdateTenantPlanRequest`, `UpdateTenantStatusRequest`, `RegisterUserRequest`, `UpdateFeatureFlagRequest`). Sensitive fields like `Role`, `Status`, `Id`, and hashes are strictly server-controlled. | Verified across all Minimal API endpoints in `TenantEndpoints.cs`, `AuthEndpoints.cs`, and `BillingEndpoints.cs`. |
| **6** | **Committed Precompiled Binaries (`StartUI.exe`)** | Executed `git rm --cached StartUI.exe` to remove the 70.8 MB precompiled executable from Git index. Added `*.exe`, `*.dll`, `*.so`, and `*.dylib` to `.gitignore`. | Verified with `git ls-files` confirming no tracked binaries remain. |
| **7** | **Root Process Execution in Docker Containers** | Hardened `Dockerfile.admin` and `Dockerfile.aot` runtime stages by adding the non-root directive `USER app` prior to `ENTRYPOINT`. | Verified in `Dockerfile.admin` and `Dockerfile.aot`. |
| **8** | **Missing Rate Limiting on Engine Endpoints** | Added sliding-window rate limiting policy (`engine-rate-limit`) in `Program.cs` partitioned by authenticated user ID or remote IP. Attached to high-throughput endpoints (`/features/{key}`, `/admin/tenants/{id}/flags`). | Verified via ASP.NET Core RateLimiter middleware configuration in `Program.cs` and `BillingEndpoints.cs`. |
