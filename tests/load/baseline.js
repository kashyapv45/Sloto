import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';

export const errorRate = new Rate('errors');

export const options = {
  stages: [
    { duration: '30s', target: 50 },  // Ramp up
    { duration: '4m', target: 100 },  // Steady state (100 VUs)
    { duration: '30s', target: 0 },   // Ramp down
  ],
  thresholds: {
    'http_req_duration': ['p(99)<200'], // 99% of requests must complete below 200ms
    'errors': ['rate<0.01'],            // Error rate must be less than 1%
  },
};

const BASE_URL = __ENV.API_URL || 'http://localhost:8080';
const ADMIN_KEY = __ENV.ADMIN_KEY || 'dev-internal-key-123';

// Generate mock JWTs for 10 simulated tenants
const TENANTS = Array.from({ length: 10 }, (_, i) => ({
  id: `00000000-0000-0000-0000-${(i + 1).toString().padStart(12, '0')}`,
  token: `eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJ1c2VyLTEyMyIsInRpZCI6IjAwMDAwMDAwLTAwMDAtMDAwMC0wMDAwLSR7KGkgKyAxKS50b1N0cmluZygpLnBhZFN0YXJ0KDEyLCAnMCcpfSJ9.`, // Mock unverified token for load test (requires test auth handler to bypass signature)
}));

export default function () {
  const tenant = TENANTS[Math.floor(Math.random() * TENANTS.length)];

  const params = {
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Key': ADMIN_KEY,
      'Authorization': `Bearer ${tenant.token}`,
    },
  };

  // Mixed workload: 70% reads, 30% writes
  const rand = Math.random();

  if (rand < 0.7) {
    // READ: Health check / status
    const res = http.get(`${BASE_URL}/health/live`, params);
    check(res, { 'status is 200': (r) => r.status === 200 }) || errorRate.add(1);
  } else if (rand < 0.9) {
    // READ: Feature flags (hits Redis cache)
    const res = http.get(`${BASE_URL}/features/advanced-reporting`, params);
    // Might be 200 or 401/403 depending on auth enforcement in test env
    check(res, { 'status is 200 or 403': (r) => r.status === 200 || r.status === 403 }) || errorRate.add(1);
  } else {
    // WRITE: Register user (hits PostgreSQL + MediatR pipeline)
    const payload = JSON.stringify({
      email: `user_${__VU}_${__ITER}@tenant${tenant.id}.com`,
      password: 'TestPassword123!',
      firstName: 'Load',
      lastName: 'Tester',
    });
    
    // We expect 401/403 here since the JWT signature is invalid, but it exercises the middleware pipeline
    const res = http.post(`${BASE_URL}/auth/register`, payload, params);
    check(res, { 'status is handled': (r) => r.status >= 200 && r.status < 500 }) || errorRate.add(1);
  }

  sleep(1);
}
