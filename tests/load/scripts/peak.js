// ─── k6 peak-load test ───────────────────────────────────────────────────────
// Simulates peak-hour traffic: 50 concurrent users sustained for 3 min.
// Focuses on authenticated member + PT endpoints across distinct VU accounts.
//
// Run:  k6 run scripts/peak.js

import { check, group } from 'k6';
import * as common from './common.js';
import * as constants from './constants.js';

export const options = {
  stages: [
    { duration: '1m', target: 25 },
    { duration: '3m', target: 50 },
    { duration: '1m', target: 0  },
  ],
  thresholds: {
    'http_req_failed': ['rate<0.05'],
    'http_req_duration': ['p(95)<1200'],
    'http_req_duration{type:read}': ['p(95)<800'],
    'checks': ['rate>0.99'],
  },
};

export default function () {
  common.resetCaches();

  // Member VU (allocated from pool of 100)
  const member = common.getMemberToken(constants.BASE_URL, __VU);
  if (member && member.accessToken) {
    check(
      common.authGet(constants.BASE_URL, '/users/me', member.accessToken),
      { 'GET /users/me: 200': (r) => r.status === 200 }
    );
    check(
      common.authGet(constants.BASE_URL, '/me/bookings', member.accessToken),
      { 'GET /me/bookings: 200': (r) => r.status === 200 }
    );
    check(
      common.authGet(constants.BASE_URL, '/trainers?limit=10', member.accessToken),
      { 'GET /trainers: 200': (r) => r.status === 200 }
    );
  }

  // PT VU (allocated from pool of 20)
  const pt = common.getPtToken(constants.BASE_URL, __VU);
  if (pt && pt.accessToken) {
    check(
      common.authGet(constants.BASE_URL, '/me/trainer-bookings', pt.accessToken),
      { 'GET /me/trainer-bookings: 200': (r) => r.status === 200 }
    );
    check(
      common.authGet(constants.BASE_URL, '/me/revenue', pt.accessToken),
      { 'GET /me/revenue: 200': (r) => r.status === 200 }
    );
  }

  // Admin VU (documented as blocked on 2FA challenge)
  const admin = common.getAdminToken(constants.BASE_URL);
  if (admin && !admin.blocked && admin.accessToken) {
    check(
      common.authGet(constants.BASE_URL, '/admin/metrics/overview', admin.accessToken),
      { 'GET /admin/metrics/overview: 200': (r) => r.status === 200 }
    );
    check(
      common.authGet(constants.BASE_URL, '/admin/users?pageSize=10', admin.accessToken),
      { 'GET /admin/users: 200': (r) => r.status === 200 }
    );
  }
}
