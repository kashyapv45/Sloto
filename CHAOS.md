# SaaS Engine — Chaos Engineering Playbook

This playbook documents how to run chaos experiments against the local `docker-compose` stack to verify system resilience, graceful degradation, and recovery.

## Prerequisites
Ensure the full stack is running locally:
```bash
docker-compose up -d
```

---

## Scenario 1: Redis Failure (Cache & Locks Offline)

**Hypothesis**: If Redis goes down, the API should remain available. Rate limiters will fail open (allow traffic), feature flags will fall back to default database values or `false`, and Hangfire jobs will pause until Redis recovers.
**Known Degradation (Token Blacklist):** The `TokenBlacklistService` falls back to an in-memory, per-process dictionary when Redis is unreachable. In a multi-instance deployment, a "logged out" token will still be accepted by instances that did not process the logout request. This is expected behavior during a Redis outage.
### Execution
1. Tail the API logs:
   ```bash
   docker-compose logs -f api
   ```
2. Kill the Redis container:
   ```bash
   docker-compose stop redis
   ```
3. Send a request to an endpoint protected by rate limiting or feature flags:
   ```bash
   curl -v http://localhost:8080/features/advanced-reporting \
     -H "Authorization: Bearer <valid-token>"
   ```

### Verification
- **Expected Result**: HTTP 200 OK. The API should log a `RedisConnectionException` as a Warning and proceed with the fallback path.
- **Queue Behavior**: Hangfire workers will sleep/retry. No new jobs will be processed.

### Recovery
1. Restart Redis:
   ```bash
   docker-compose start redis
   ```
2. **Verification**: API should automatically reconnect and resume caching. Hangfire should immediately resume processing the job queue.

---

## Scenario 2: Primary Database Failure (PostgreSQL Offline)

**Hypothesis**: If the primary database goes down, all read/write operations dependent on persistence will fail fast with HTTP 503 Service Unavailable or HTTP 500, rather than hanging indefinitely and exhausting thread pools.

### Execution
1. Kill the PostgreSQL container:
   ```bash
   docker-compose stop postgres
   ```
2. Send a read request:
   ```bash
   curl -v http://localhost:8080/health/ready
   ```

### Verification
- **Expected Result**: The `/health/ready` endpoint should return HTTP 503 Service Unavailable. Standard API requests should return HTTP 500 (handled by global exception middleware logging an `NpgsqlException`).
- **Connection Pools**: Ensure the API does not freeze or block indefinitely.

### Recovery
1. Restart PostgreSQL:
   ```bash
   docker-compose start postgres
   ```
2. **Verification**: The API connection pool should automatically heal. The `/health/ready` endpoint should return HTTP 200 OK within 15 seconds.

---

## Scenario 3: API Process Crash During Job Execution

**Hypothesis**: If the API container (which hosts the Hangfire workers) crashes mid-job, the job should not be lost. It should remain in the `processing` state until the invisibility timeout expires, then revert to `enqueued` and be processed by the next available worker.

### Execution
1. Enqueue a long-running job (e.g., a massive GDPR export).
2. While the job is executing (monitor via Hangfire Dashboard), kill the API container:
   ```bash
   docker-compose kill api
   ```

### Verification
- **Expected Result**: The job is interrupted. In Redis, the job state remains `processing`.
- **Note**: By default, Hangfire's invisibility timeout handles worker crashes.

### Recovery
1. Start the API container again:
   ```bash
   docker-compose start api
   ```
2. **Verification**: Wait for the invisibility timeout (configurable, default usually 5-30 mins depending on storage config, set to 5m in our setup). The job should automatically transition back to the `default` queue and execute successfully.

---

## Automated Chaos Testing

For automated CI/CD chaos testing, consider integrating **Chaos Mesh** or **Gremlin** into the Kubernetes staging cluster to randomly terminate pods and inject network latency while the `k6` load test suite is running.
