import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';

export const dataLeakRate = new Rate('data_leaks');
export const errorRate = new Rate('errors');

export const options = {
  vus: 50,
  duration: '1m',
  thresholds: {
    'data_leaks': ['rate==0'], // CRITICAL: Must be exactly zero leaks
    'errors': ['rate<0.01'],
  },
};

const BASE_URL = __ENV.API_URL || 'http://localhost:8080';
const ADMIN_KEY = __ENV.ADMIN_KEY || 'dev-internal-key-123';

// Simulating two distinct tenants
const TENANTS = [
  {
    id: '00000000-0000-0000-0000-111111111111',
    token: `eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJ1c2VyLTExMSIsInRpZCI6IjAwMDAwMDAwLTAwMDAtMDAwMC0wMDAwLTExMTExMTExMTExMSJ9.`,
  },
  {
    id: '00000000-0000-0000-0000-222222222222',
    token: `eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJ1c2VyLTIyMiIsInRpZCI6IjAwMDAwMDAwLTAwMDAtMDAwMC0wMDAwLTIyMjIyMjIyMjIyMiJ9.`,
  }
];

export default function () {
  // Alternate VUs between the two tenants
  const tenantIndex = __VU % 2;
  const tenant = TENANTS[tenantIndex];
  const otherTenant = TENANTS[tenantIndex === 0 ? 1 : 0];

  const params = {
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Key': ADMIN_KEY,
      'Authorization': `Bearer ${tenant.token}`,
    },
  };

  // Attempt to fetch audit events (which are tenant-scoped)
  const res = http.get(`${BASE_URL}/audit/events?pageSize=10`, params);

  const success = check(res, {
    'status is 200 or 401': (r) => r.status === 200 || r.status === 401,
  });

  if (!success) {
    errorRate.add(1);
  }

  // If we got a 200 OK (meaning the mock token was accepted in test env)
  if (res.status === 200) {
    try {
      const body = res.json();
      
      // Look for any data belonging to the *other* tenant
      let leakDetected = false;
      if (body && body.items && Array.isArray(body.items)) {
        for (const item of body.items) {
          if (item.tenantId === otherTenant.id) {
            leakDetected = true;
            break;
          }
        }
      }

      if (leakDetected) {
        dataLeakRate.add(1);
        console.error(`[CRITICAL] Data leak detected! VU ${__VU} (Tenant ${tenant.id}) received data for Tenant ${otherTenant.id}`);
      } else {
        dataLeakRate.add(0);
      }
    } catch (e) {
      errorRate.add(1);
    }
  }

  sleep(0.5);
}
