# Sloto (Next-Gen SaaS Engine) — Independent Code Audit

> Based on a direct inspection of the repository source (not just the README claims), as of the `main` branch, July 2026.
> Repo: `kashyapv45/Sloto` — 1 star, 0 forks, 6 commits, single contributor.

This document separates what the project **claims**, what's **actually true**, and what needs to change before this could be trusted with real traffic.

---

## 1. Project Summary

A .NET 9 multi-tenant B2B SaaS backend template. Modular monolith, vertical-slice features (Tenancy, Billing, Identity, Audit), Dapper for hot paths + EF Core for admin, Hangfire + transactional outbox for background jobs, OpenTelemetry/Serilog for observability, Native AOT target for the public API.

**Verdict up front:** the architectural design is genuinely sound and well above "tutorial project" quality. But it is currently a skeleton with several concrete, verifiable bugs and gaps that make the "production-grade" label premature.

---

## 2. Confirmed Bugs

### 2.1 JWT signing key is not persistent across restarts or instances
`src/SaasEngine.Api/Infrastructure/Security/JwtTokenGenerator.cs`

- If `Jwt:PrivateKeyPem` is not set in configuration (and it is **not** set in `appsettings.Development.json`), the constructor falls back to `RSA.Create(2048)` — a **fresh, random RSA key generated every time the process starts.**
- The generator is registered as a DI singleton (`Program.cs` line 150), so it's stable *within one running instance*. But:
  - Restarting the process invalidates every previously issued token.
  - Running more than one instance behind a load balancer (the normal case for anything "enterprise" or "production-grade") means each instance signs with a different key — tokens issued by instance A will fail signature validation on instance B.
- There's also a dead, unused `DevRsaKey` static class sitting next to this — leftover code from an earlier version, not referenced anywhere.
- **Fix needed:** load the private key from a real secret store (the code already has an `ISecretStore` interface in the Domain layer — it just isn't wired up here) and fail fast at startup if it's missing, rather than silently generating a throwaway key.

### 2.2 Outbox events can get stuck as "pending" after the poller runs
`src/SaasEngine.Api/Infrastructure/Outbox/OutboxPollerJob.cs`, corroborated by the committed `test_run.log`

- The committed log shows: event inserted → poller executes → event **still shows `status=pending`, `attempts=0`** afterward. It was never picked up.
- The poller only queries tenants where `status = 'active'`. If a newly created tenant isn't marked active at the moment the poller runs (a timing/ordering issue between tenant creation and the first outbox flush), its events are silently skipped — not retried, not failed, just invisible.
- This directly undermines the "Transactional Outbox pattern for resilience" claim in the README, since the resilience guarantee (every event eventually gets delivered) doesn't hold in this observed case.
- **Fix needed:** the poller should not gate on tenant status at all for outbox delivery (a suspended tenant can still have pending events that need to resolve), or at minimum, log/alert when events are skipped due to tenant status rather than leaving them silently pending forever.

### 2.3 Empty test projects despite being advertised as "extensive"
`tests/SaasEngine.UnitTests/`, `tests/SaasEngine.ArchitectureTests/`

- Both projects contain **only a `.csproj` file** — package references for xUnit, FluentAssertions, NSubstitute, NetArchTest.Rules are wired up, but there is not a single test file in either project.
- The README's claim of an "Extensive integration test suite" is only true of the `IntegrationTests` project (~1,700 lines across 6 files, which does look real and reasonably thorough).
- **Fix needed:** either write the unit/architecture tests, or stop implying they exist. Architecture tests in particular are cheap to write given `NetArchTest.Rules` is already referenced (e.g., "Domain must not reference Api", "only X classes may implement IPlanEnforcedRequest") and would directly enforce the boundaries the README describes.

### 2.4 Leaked AI coding-agent debug artifacts committed to the repo
`extract.txt`, `extract_utf8.txt`

- These are raw tool-call transcripts from an AI coding assistant session — JSON blobs like `"source":"MODEL","type":"VIEW_FILE"` referencing a local path (`k:/Work/Project/NextGen/...`).
- This isn't project documentation or source code — it's an accidental commit of the author's AI-agent session log, and it's a sign the repo wasn't reviewed for "what am I about to commit" before pushing.
- Same category issue: `test_run.log` is a raw console dump from a manual debug session, not something meant to be permanent project history.
- **Fix needed:** remove these files, add `extract*.txt` / `*.log` to `.gitignore`, and review the rest of git history for anything else that leaked (config, credentials, etc.).

### 2.5 CI threshold doesn't match documented target
`.github/workflows/ci.yml` vs `BENCHMARKS.md`

- `BENCHMARKS.md` states a binary-size target of **< 20 MB**.
- The actual CI check fails the build only if the binary exceeds **60 MB**, but the error message printed says `"AOT binary exceeds 20MB target"` — the enforced number and the message don't agree, and neither is verified against a real measured result (the benchmarks doc lists targets, not actual measurements).
- **Fix needed:** decide on one real number, verify it by actually running `dotnet publish` and checking, and make the CI threshold and the doc agree.

---

## 3. Gaps / Design Limitations (not bugs, but worth flagging)

- **Token blacklist (logout) has no cross-instance guarantee without Redis.** `TokenBlacklistService` falls back to a per-process, in-memory dictionary if Redis is unreachable. That's a reasonable *documented* fallback for local dev, but if Redis goes down in a real multi-instance deployment, a "logged out" token would still work against instances that didn't see the logout request. This should be called out explicitly in `CHAOS.md` as a known degradation, not left implicit.
- **`MediatR`'s assembly scanning and Hangfire's reflection-based job serialization** are both honestly documented in `BENCHMARKS.md` as *not* truly AOT-safe, just "working in practice." That's good transparency, but it means the "full Native AOT compatibility" headline claim in the README is overstated relative to the more careful language in the benchmarks doc itself.
- **Dev credentials (`DevPassword123!`, etc.) are committed in `appsettings.Development.json`.** Low severity since they're clearly local-only placeholders, but it's still a habit worth breaking — use user-secrets or environment variables even for local dev config, so nobody has to remember which passwords are "the fake ones."
- **Single contributor, 6 commits, no CI run history visible, no releases/tags.** None of this is a flaw in the code, but it means none of the above has been battle-tested by a second pair of eyes or real traffic yet.

---

## 4. What's Actually Good (for balance)

- The **vertical-slice / modular monolith structure** is consistently applied — Domain has zero dependencies, Contracts are separate from Domain entities, Admin is cleanly split from the public API. This is a harder discipline to maintain than it looks and it's maintained here.
- **`RUNBOOK.md` and `CHAOS.md`** are specific and operational — real alert names, real diagnostic commands, real recovery steps — not generic filler. Most solo/hobby projects don't have this at all.
- The **integration test suite** that does exist is substantive, not just smoke tests (plan enforcement, tenant isolation, background jobs, and auth all have dedicated, sizeable test files).
- The **outbox poller's core design** (per-tenant distributed lock, retry with attempt counting, terminal "failed" state after 5 attempts) is a legitimate resilience pattern — the bug in §2.2 is a real problem, but the pattern it's built on is the right one.

---

## 5. Priority Order for Fixes

If someone wanted to actually take this from "skeleton" to "usable foundation," roughly in order:

1. Fix the JWT signing key persistence issue (§2.1) — this is an auth-breaking bug, highest priority.
2. Investigate and fix the outbox "stuck pending" issue (§2.2) — this breaks the core reliability promise of the architecture.
3. Remove leaked debug artifacts and clean git history (§2.4) — quick, and a trust/hygiene issue for anyone evaluating the repo.
4. Write real unit and architecture tests (§2.3) — cheap relative to the value, and the packages are already there.
5. Reconcile the CI threshold vs. documented benchmark target (§2.5) — small fix, but currently the CI would happily pass a binary 3x the documented target.
6. Document the Redis-down blacklist degradation explicitly in `CHAOS.md`, matching the honesty already shown in `BENCHMARKS.md`.

---

*This audit reflects a point-in-time review of the public repository content. It does not include a full security review, dependency vulnerability scan, or load testing — those would be reasonable next steps before any production use.*
