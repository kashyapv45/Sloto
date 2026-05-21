import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';

export const errorRate = new Rate('errors');

export const options = {
  stages: [
    { duration: '30s', target: 1000 }, // Extremely fast ramp-up to 1000 VUs
    { duration: '1m', target: 1000 },  // Hold for 1 minute
    { duration: '30s', target: 0 },    // Fast ramp-down
  ],
  thresholds: {
    // Under heavy spike, latency can degrade slightly, but must recover
    'http_req_duration': ['p(95)<500'], 
    'errors': ['rate<0.05'], // Accept up to 5% failure rate (e.g. rate limiter 429s) under extreme spike
  },
};

const BASE_URL = __ENV.API_URL || 'http://localhost:8080';

export default function () {
  // Simulate high-throughput unauthenticated read (e.g., hitting rate limiters)
  const params = {
    headers: {
      'Content-Type': 'application/json',
    },
  };

  const res = http.get(`${BASE_URL}/health/live`, params);
  
  // We expect 200 OK or 429 Too Many Requests if rate limiter kicks in
  check(res, {
    'status is 200 or 429': (r) => r.status === 200 || r.status === 429,
  }) || errorRate.add(1);

  // Short sleep to simulate aggressive client retry/polling
  sleep(0.1);
}
