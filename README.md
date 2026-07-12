# Next-Gen SaaS Engine

A production-grade, multi-tenant B2B SaaS engine built on **.NET 9**.

This project provides a complete architectural foundation for enterprise software, featuring Native AOT compatibility (see benchmarks for known limitations), comprehensive OpenTelemetry observability, multi-tier tenant data isolation, and GDPR compliance out-of-the-box.

## 🏗️ Architecture

The application is built using a **Modular Monolith** architecture. The codebase is organized into vertical slices by feature, enforcing strict boundaries while deploying as a single process for operational simplicity and maximum performance.

### Key Technical Decisions
- **Data Access:** Fast-path operations use **Dapper** (fully AOT compatible), while admin and migrations use **EF Core**.
- **Tenancy:** Hybrid routing supporting shared databases for Standard/Pro tiers and physically isolated databases for Enterprise tiers.
- **Background Jobs:** **Hangfire** backed by Redis for job orchestration, utilizing the Transactional Outbox pattern for resilience.
- **Telemetry:** **OpenTelemetry** traces, metrics, and Serilog structured logs ready for ingestion into Grafana or Datadog.

## 🚀 Quick Start

### 1. Prerequisites
- Docker & Docker Compose
- .NET 9 SDK

### 2. Run Infrastructure
Start the PostgreSQL and Redis containers:
```bash
docker-compose up -d
```

### 3. Run the API
Run the Native AOT optimized API:
```bash
dotnet run --project src/SaasEngine.Api/SaasEngine.Api.csproj -c Release
```

The API will be available at `http://localhost:8080`.
- Health Check: `curl http://localhost:8080/health/live`
- Readiness Check: `curl http://localhost:8080/health/ready`
- OpenAPI JSON: `curl http://localhost:8080/openapi/v1.json`
- API Documentation UI (Scalar): `http://localhost:8080/scalar/v1`
- Hangfire Dashboard: `http://localhost:8080/hangfire`

## 🗄️ Project Structure

- `src/SaasEngine.Api`: The public-facing Minimal API. Configured for `.NET Native AOT`.
- `src/SaasEngine.Domain`: Pure domain entities, value objects, and domain events. Zero dependencies.
- `src/SaasEngine.Contracts`: Shared DTOs and API response models.
- `src/SaasEngine.Admin`: Internal back-office API and EF Core migrations (non-AOT).
- `tests/`: Comprehensive test suite including Unit, Architecture (NetArchTest), and Integration tests (utilizing `Testcontainers` for true PostgreSQL and Redis parity).

## 📚 Documentation
- [Observability Runbook](RUNBOOK.md)
- [Chaos Engineering Playbook](CHAOS.md)
- [Native AOT Benchmarks](BENCHMARKS.md)
