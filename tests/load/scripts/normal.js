// ─── k6 normal-load test ──────────────────────────────────────────────────────
// Simulates typical day traffic: 10 concurrent users over 2 minutes.
// Validates typical happy path for member and PT roles with isolated VU accounts.
//
// Run:  k6 run scripts/normal.js

import { check, group, sleep } from 'k6';
import http from 'k6/http';
import * as common from './common.js';
import * as constants from './constants.js';

export const options = {
  stages: [
    { duration: '30s', target: 5 },   // ramp up
    { duration: '1m',  target: 10 },  // sustain load
    { duration: '30s', target: 0 },   // ramp down
  ],
  thresholds: {
    'http_req_failed': ['rate<0.02'],                // <2% errors
    'http_req_duration': ['p(95)<800', 'p(99)<1500'], // combined latency
    'http_req_duration{type:read}': ['p(95)<500'],   // read SLA: p95 < 500ms
    'checks': ['rate>0.99'],                         // >99% checks pass
  },
};

export default function () {
  common.resetCaches();
  common.checkHealth(constants.BASE_URL);

  // ── Member journey (VU-isolated account) ────────────────────────────────────
  const member = common.getMemberToken(constants.BASE_URL, __VU);

  if (member && member.accessToken) {
    group('Member: profile + bookings + trainers', () => {
      const me = common.authGet(constants.BASE_URL, '/users/me', member.accessToken);
      check(me, { 'GET /users/me: 200': (r) => r.status === 200 });

      const bookings = common.authGet(constants.BASE_URL, '/me/bookings', member.accessToken);
      check(bookings, { 'GET /me/bookings: 200': (r) => r.status === 200 });

      const trainers = common.authGet(constants.BASE_URL, '/trainers?limit=10', member.accessToken);
      check(trainers, { 'GET /trainers: 200': (r) => r.status === 200 });

      const notifications = common.authGet(constants.BASE_URL, '/me/notifications', member.accessToken);
      check(notifications, { 'GET /me/notifications: 200': (r) => r.status === 200 });
    });
  }

  // ── PT journey (VU-isolated account) ────────────────────────────────────────
  const pt = common.getPtToken(constants.BASE_URL, __VU);

  if (pt && pt.accessToken) {
    group('PT: trainer-bookings + revenue + availability', () => {
      const tBookings = common.authGet(constants.BASE_URL, '/me/trainer-bookings', pt.accessToken);
      check(tBookings, { 'GET /me/trainer-bookings: 200': (r) => r.status === 200 });

      const revenue = common.authGet(constants.BASE_URL, '/me/revenue', pt.accessToken);
      check(revenue, { 'GET /me/revenue: 200': (r) => r.status === 200 });

      const from = encodeURIComponent(new Date().toISOString());
      const to = encodeURIComponent(new Date(Date.now() + 86400000 * 14).toISOString());
      const availability = common.authGet(constants.BASE_URL, `/trainers/${pt.userId}/availability?from=${from}&to=${to}`, pt.accessToken);
      check(availability, { 'GET /trainers/:id/availability: 200': (r) => r.status === 200 });
    });
  }

  // Realistic user pacing between page views
  sleep(1);
}
