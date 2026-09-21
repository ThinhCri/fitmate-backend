// ─── k6 stress test ───────────────────────────────────────────────────────────
// Gradually increase VUs from 10 to 100 over 5 minutes to find the breaking
// point and observe how the system degrades under extreme load.
//
// Run:  k6 run scripts/stress.js

import { check } from 'k6';
import * as common from './common.js';
import * as constants from './constants.js';

export const options = {
  stages: [
    { duration: '1m',  target: 10  },
    { duration: '1m',  target: 30  },
    { duration: '1m',  target: 60  },
    { duration: '2m',  target: 100 },
    { duration: '30s', target: 0   },
  ],
  thresholds: {
    // Allow elevated errors — we want to observe degradation
    http_req_failed:  ['rate<0.20'],
    http_req_duration: ['p(95)<5000'],
  },
};

export default function () {
  common.resetCaches();

  const member = common.getMemberToken(constants.BASE_URL);
  const pt = common.getPtToken(constants.BASE_URL);
  const admin = common.getAdminToken(constants.BASE_URL);

  // Member endpoints
  check(
    common.authGet(constants.BASE_URL, '/users/me', member.accessToken),
    { 'stress /users/me': (r) => r.status >= 200 && r.status < 500 }
  );
  check(
    common.authGet(constants.BASE_URL, '/me/bookings', member.accessToken),
    { 'stress /me/bookings': (r) => r.status >= 200 && r.status < 500 }
  );
  check(
    common.authGet(constants.BASE_URL, '/trainers?pageSize=10', member.accessToken),
    { 'stress /trainers': (r) => r.status >= 200 && r.status < 500 }
  );

  // PT endpoints
  check(
    common.authGet(constants.BASE_URL, '/me/trainer-bookings', pt.accessToken),
    { 'stress /me/trainer-bookings': (r) => r.status >= 200 && r.status < 500 }
  );
  check(
    common.authGet(constants.BASE_URL, '/me/revenue', pt.accessToken),
    { 'stress /me/revenue': (r) => r.status >= 200 && r.status < 500 }
  );

  // Admin endpoints
  check(
    common.authGet(constants.BASE_URL, '/admin/metrics/overview', admin.accessToken),
    { 'stress /admin/metrics/overview': (r) => r.status >= 200 && r.status < 500 }
  );
}
