# SaaS Engine — Native AOT Benchmarks

## Build Configuration

| Property | Value |
|----------|-------|
| Runtime | .NET 9.0 |
| Publish Mode | Native AOT (`PublishAot=true`) |
| Trimming | Full (`IsAotCompatible=true`) |
| Globalization | Invariant (`InvariantGlobalization=true`) |
| Target RID | `linux-x64` (Docker), `win-x64` (local dev) |
| Dockerfile | `Dockerfile.aot` (distroless final stage) |

## AOT Compatibility Status

### ✅ Fully AOT-Compatible

| Component | Strategy |
|-----------|----------|
| **JSON Serialization** | Source-generated `AppJsonSerializerContext` — 34 registered types, zero reflection |
| **Pipeline Behaviors** | Interface-based dispatch (`IAuditableRequest`, `IPlanEnforcedRequest`) — no `GetCustomAttribute` |
| **Middleware** | `IMiddleware` pattern — strongly-typed invocation, no convention-based reflection |
| **Dapper Type Handlers** | Explicit `SqlMapper.AddTypeHandler<T>()` registrations for `Guid`, `DateTimeOffset`, and nullable variants |
| **FluentValidation** | Explicit `AddScoped<IValidator<T>, TValidator>()` — no assembly scanning |
| **Npgsql** | AOT-compatible since v7+ |
| **StackExchange.Redis** | AOT-compatible |
| **Microsoft.IdentityModel** | v8.x — AOT-compatible |
| **Cryptography** | `RSA.Create()`, `SHA256.HashData()`, PBKDF2 — all AOT-safe |

### ⚠️ Known Limitations

| Component | Status | Mitigation |
|-----------|--------|------------|
| **MediatR 12** | Assembly scanning at startup | Uses `RegisterServicesFromAssembly` which discovers types at startup, not at request time. All handler types are directly referenced in the assembly, so the trimmer preserves them. Works in practice with AOT but is not guaranteed by the library. |
| **Hangfire 1.8** | Serializes `MethodInfo` for job storage | Fundamentally reflection-based. The `BackgroundTaskDispatcher` and `IBackgroundTaskHandler` pattern provide an AOT-safe alternative for job dispatch. Hangfire is used only for scheduling/persistence, not for type activation. |

## Trimmed Publish Results

```
Build: dotnet build -c Release
Result: 0 warnings, 0 errors

Trimming Analyzer: <IsAotCompatible>true</IsAotCompatible> enabled
Result: 0 trim warnings in SaasEngine.Api source code
```

> **Note:** Full Native AOT linking (`dotnet publish -r linux-x64`) requires the C++ Desktop Development
> workload (MSVC linker on Windows) or a Linux build environment (Docker). The CI pipeline
> (`Dockerfile.aot`) produces the final AOT binary on `linux-x64`.

## Performance Targets

| Metric | Target | Measurement Method |
|--------|--------|--------------------|
| **Binary Size** | < 20 MB | `ls -lh ./publish/SaasEngine.Api` after `dotnet publish -r linux-x64` |
| **Cold Start** | < 10 ms | `time curl http://localhost:8080/health/live` on fresh container start |
| **Memory (Idle)** | < 50 MB | Container RSS after startup, no active requests |
| **Memory (100 req/s)** | < 100 MB | Container RSS under sustained k6 load |
| **p99 Latency** | < 200 ms | k6 baseline test at 500 req/s |

## Reflection Audit Summary

### Before Phase 6

| Location | Issue |
|----------|-------|
| `AuditBehavior.cs` | `request.GetType().GetCustomAttribute<AuditableAttribute>()` |
| `PlanEnforcementBehavior.cs` | `typeof(TRequest).GetCustomAttributes<EnforcePlanAttribute>()` |
| `TenantResolutionMiddleware.cs` | Convention-based `UseMiddleware<T>()` |
| `Program.cs` | `AddValidatorsFromAssembly()` assembly scanning |
| `AuditBehavior.cs` | `JsonSerializer.Serialize(state, Default.Object)` unsafe fallback |
| All Dapper sites | No explicit type handler registration |

### After Phase 6

| Location | Resolution |
|----------|-----------|
| `AuditBehavior.cs` | `request is IAuditableRequest` interface check |
| `PlanEnforcementBehavior.cs` | `request is IPlanEnforcedRequest` interface check |
| `TenantResolutionMiddleware.cs` | `IMiddleware` with DI constructor injection |
| `Program.cs` | Explicit `AddScoped<IValidator<T>, TValidator>()` |
| `AuditBehavior.cs` | Returns `null` for unknown types (AOT-safe) |
| `DapperTypeHandlers.cs` | Explicit `SqlMapper.AddTypeHandler<T>()` at startup |

**Result: Zero `System.Reflection` imports in SaasEngine.Api source code.**
