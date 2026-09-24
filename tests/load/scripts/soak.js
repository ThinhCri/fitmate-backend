// ─── k6 soak test ────────────────────────────────────────────────────────────
// Sustained moderate load (15 VUs) for 15 minutes.
// Detects memory leaks, connection-pool exhaustion, and resource exhaustion.
//
// Run:  k6 run scripts/soak.js

import { check, sleep } from 'k6';
import * as common from './common.js';
import * as constants from './constants.js';

export const options = {
  stages: [
    { duration: '15m', target: 15 },
  ],
  thresholds: {
    'http_req_failed':  ['rate<0.03'],
    'http_req_duration': ['p(95)<1000', 'p(99)<2000'],
    'checks': ['rate>0.99'],
  },
};

export default function () {
  common.resetCaches();

  const member = common.getMemberToken(constants.BASE_URL, __VU);
  const pt = common.getPtToken(constants.BASE_URL, __VU);
  const admin = common.getAdminToken(constants.BASE_URL);

  // Member
  if (member && member.accessToken) {
    check(
      common.authGet(constants.BASE_URL, '/users/me', member.accessToken),
      { 'soak /users/me: 200': (r) => r.status === 200 }
    );
    check(
      common.authGet(constants.BASE_URL, '/me/bookings', member.accessToken),
      { 'soak /me/bookings: 200': (r) => r.status === 200 }
    );
    check(
      common.authGet(constants.BASE_URL, '/me/notifications', member.accessToken),
      { 'soak /me/notifications: 200': (r) => r.status === 200 }
    );
  }

  // PT
  if (pt && pt.accessToken) {
    check(
      common.authGet(constants.BASE_URL, '/me/trainer-bookings', pt.accessToken),
      { 'soak /me/trainer-bookings: 200': (r) => r.status === 200 }
    );
    check(
      common.authGet(constants.BASE_URL, '/me/revenue', pt.accessToken),
      { 'soak /me/revenue: 200': (r) => r.status === 200 }
    );
  }

  // Admin (if session available)
  if (admin && !admin.blocked && admin.accessToken) {
    check(
      common.authGet(constants.BASE_URL, '/admin/metrics/overview', admin.accessToken),
      { 'soak /admin/metrics/overview: 200': (r) => r.status === 200 }
    );
    check(
      common.authGet(constants.BASE_URL, '/admin/users?pageSize=5', admin.accessToken),
      { 'soak /admin/users: 200': (r) => r.status === 200 }
    );
  }

  // Small think time between iterations
  sleep(0.5);
}
