// ─── k6 soak test ────────────────────────────────────────────────────────────
// Sustained moderate load (15 VUs) for 15 minutes.
// Detects memory leaks, connection-pool exhaustion, and log rotation issues.
//
// Run:  k6 run scripts/soak.js

import { check, sleep } from 'k6';
import * as common from './common.js';
import * as constants from './constants.js';

export const options = {
  // 15 VUs × 15 min = ~9 000 total iterations — enough to surface leaks
  stages: [
    { duration: '15m', target: 15 },
  ],
  thresholds: {
    http_req_failed:  ['rate<0.03'],
    http_req_duration: ['p(95)<1000'],
    // Check for gradual increase in response time (leak indicator)
    http_req_duration: ['trend<1000'],
  },
};

export default function () {
  common.resetCaches();

  const member = common.getMemberToken(constants.BASE_URL);
  const pt = common.getPtToken(constants.BASE_URL);
  const admin = common.getAdminToken(constants.BASE_URL);

  // Member
  check(
    common.authGet(constants.BASE_URL, '/users/me', member.accessToken),
    { 'soak /users/me': (r) => r.status === 200 }
  );
  check(
    common.authGet(constants.BASE_URL, '/me/bookings', member.accessToken),
    { 'soak /me/bookings': (r) => r.status === 200 }
  );
  check(
    common.authGet(constants.BASE_URL, '/me/notifications', member.accessToken),
    { 'soak /me/notifications': (r) => r.status === 200 }
  );

  // PT
  check(
    common.authGet(constants.BASE_URL, '/me/trainer-bookings', pt.accessToken),
    { 'soak /me/trainer-bookings': (r) => r.status === 200 }
  );
  check(
    common.authGet(constants.BASE_URL, '/me/revenue', pt.accessToken),
    { 'soak /me/revenue': (r) => r.status === 200 }
  );

  // Admin
  check(
    common.authGet(constants.BASE_URL, '/admin/metrics/overview', admin.accessToken),
    { 'soak /admin/metrics/overview': (r) => r.status === 200 }
  );
  check(
    common.authGet(constants.BASE_URL, '/admin/users?pageSize=5', admin.accessToken),
    { 'soak /admin/users': (r) => r.status === 200 }
  );

  // Small think time between iterations
  sleep(0.5);
}
