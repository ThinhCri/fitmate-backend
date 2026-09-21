// ─── k6 peak-load test ───────────────────────────────────────────────────────
// Simulates peak-hour traffic: 50 concurrent users sustained for 3 min.
// Focuses on authenticated member + PT endpoints.
//
// Run:  k6 run scripts/peak.js

import { check } from 'k6';
import * as common from './common.js';
import * as constants from './constants.js';

export const options = {
  stages: [
    { duration: '1m',  target: 25 },
    { duration: '3m',  target: 50 },
    { duration: '1m',  target: 0  },
  ],
  thresholds: {
    http_req_failed:  ['rate<0.05'],
    http_req_duration: ['p(95)<1200'],
  },
};

export default function () {
  common.resetCaches();

  // Member VU
  const member = common.getMemberToken(constants.BASE_URL);

  check(
    common.authGet(constants.BASE_URL, '/users/me', member.accessToken),
    { 'GET /users/me': (r) => r.status === 200 }
  );
  check(
    common.authGet(constants.BASE_URL, '/me/bookings', member.accessToken),
    { 'GET /me/bookings': (r) => r.status === 200 }
  );
  check(
    common.authGet(constants.BASE_URL, '/trainers?pageSize=10', member.accessToken),
    { 'GET /trainers': (r) => r.status === 200 }
  );

  // PT VU
  const pt = common.getPtToken(constants.BASE_URL);

  check(
    common.authGet(constants.BASE_URL, '/me/trainer-bookings', pt.accessToken),
    { 'GET /me/trainer-bookings': (r) => r.status === 200 }
  );
  check(
    common.authGet(constants.BASE_URL, '/me/revenue', pt.accessToken),
    { 'GET /me/revenue': (r) => r.status === 200 }
  );

  // Admin VU
  const admin = common.getAdminToken(constants.BASE_URL);

  check(
    common.authGet(constants.BASE_URL, '/admin/metrics/overview', admin.accessToken),
    { 'GET /admin/metrics/overview': (r) => r.status === 200 }
  );
  check(
    common.authGet(constants.BASE_URL, '/admin/users?pageSize=10', admin.accessToken),
    { 'GET /admin/users': (r) => r.status === 200 }
  );
}
