// ─── k6 spike test ────────────────────────────────────────────────────────────
// Instant surge: 5 → 100 VUs in 30s, back to 5 in 30s.
// Verifies the system handles sudden traffic spikes without crashing.
//
// Run:  k6 run scripts/spike.js

import { check } from 'k6';
import * as common from './common.js';
import * as constants from './constants.js';

export const options = {
  stages: [
    { duration: '30s', target: 5   },   // baseline
    { duration: '30s', target: 100 },   // spike up
    { duration: '1m',  target: 100 },   // sustain spike
    { duration: '30s', target: 5   },   // spike down
  ],
  thresholds: {
    http_req_failed:  ['rate<0.10'],  // allow 10% errors under extreme load
    http_req_duration: ['p(95)<3000'],
  },
};

export default function () {
  common.resetCaches();

  const member = common.getMemberToken(constants.BASE_URL);

  check(
    common.authGet(constants.BASE_URL, '/users/me', member.accessToken),
    { 'spike /users/me': (r) => r.status === 200 || r.status === 503 }
  );
  check(
    common.authGet(constants.BASE_URL, '/me/bookings', member.accessToken),
    { 'spike /me/bookings': (r) => r.status === 200 || r.status === 503 }
  );
}
