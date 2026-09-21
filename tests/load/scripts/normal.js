// ─── k6 normal-load test ──────────────────────────────────────────────────────
// Simulates typical day traffic: 10 concurrent users, ramping up over 2 min,
// sustaining 2 min.  Validates the happy path for member, PT, and admin roles.
//
// Run:  k6 run scripts/normal.js

import { check } from 'k6';
import http from 'k6/http';
import * as common from './common.js';
import * as constants from './constants.js';

export const options = {
  stages: [
    { duration: '1m',  target: 5  },
    { duration: '2m',  target: 10 },
    { duration: '2m',  target: 10 },
    { duration: '30s', target: 0  },
  ],
  thresholds: {
    http_req_failed:  ['rate<0.02'],  // <2% error
    http_req_duration: ['p(95)<800'],
    http_req_duration: ['p(99)<1500'],
  },
};

export default function () {
  common.resetCaches();
  common.checkHealth(constants.BASE_URL);

  // ── Member journey ──────────────────────────────────────────────────────────
  const member = common.getMemberToken(constants.BASE_URL);

  group('Member: profile + bookings + trainers', () => {
    const me = common.authGet(constants.BASE_URL, '/users/me', member.accessToken);
    check(me, { 'GET /users/me': (r) => r.status === 200 });

    const bookings = common.authGet(constants.BASE_URL, '/me/bookings', member.accessToken);
    check(bookings, { 'GET /me/bookings': (r) => r.status === 200 });

    const trainers = http.get(`${constants.BASE_URL}${constants.API_PREFIX}/trainers?pageSize=10`, {
      headers: { Authorization: `Bearer ${member.accessToken}` },
    });
    check(trainers, { 'GET /trainers': (r) => r.status === 200 });

    const notifications = common.authGet(constants.BASE_URL, '/me/notifications', member.accessToken);
    check(notifications, { 'GET /me/notifications': (r) => r.status === 200 });
  });

  // ── PT journey ──────────────────────────────────────────────────────────────
  const pt = common.getPtToken(constants.BASE_URL);

  group('PT: trainer-bookings + revenue + availability', () => {
    const tBookings = common.authGet(constants.BASE_URL, '/me/trainer-bookings', pt.accessToken);
    check(tBookings, { 'GET /me/trainer-bookings': (r) => r.status === 200 });

    const revenue = common.authGet(constants.BASE_URL, '/me/revenue', pt.accessToken);
    check(revenue, { 'GET /me/revenue': (r) => r.status === 200 });

    const availability = common.authGet(constants.BASE_URL, '/me/availability', pt.accessToken);
    check(availability, { 'GET /me/availability': (r) => r.status === 200 });
  });

  // ── Admin journey ───────────────────────────────────────────────────────────
  const admin = common.getAdminToken(constants.BASE_URL);

  group('Admin: metrics + users + bookings', () => {
    const metrics = common.authGet(constants.BASE_URL, '/admin/metrics/overview', admin.accessToken);
    check(metrics, { 'GET /admin/metrics/overview': (r) => r.status === 200 });

    const users = common.authGet(constants.BASE_URL, '/admin/users?pageSize=10', admin.accessToken);
    check(users, { 'GET /admin/users': (r) => r.status === 200 });

    const adminBookings = common.authGet(constants.BASE_URL, '/admin/bookings?pageSize=10', admin.accessToken);
    check(adminBookings, { 'GET /admin/bookings': (r) => r.status === 200 });

    const finance = common.authGet(constants.BASE_URL, '/admin/finance/overview', admin.accessToken);
    check(finance, { 'GET /admin/finance/overview': (r) => r.status === 200 });
  });
}
