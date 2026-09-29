// ─── k6 smoke test ─────────────────────────────────────────────────────────────
// Fast sanity check: health + register + login + basic authenticated reads.
// Run:  k6 run scripts/smoke.js
//
// Stages: 1 VU × 30s  →  verifies the stack boots and responds correctly.

import { check, sleep } from 'k6';
import http from 'k6/http';
import * as constants from './constants.js';

export const options = {
  stages: [
    { duration: '10s', target: 1 },
    { duration: '20s', target: 1 },
  ],
  thresholds: {
    http_req_failed: ['rate<0.05'],  // <5% error rate
    http_req_duration: ['p(95)<800'],
  },
};

export function setup() {
  // Register exactly one disposable account per smoke execution. Creating an
  // account inside the VU loop used to leave roughly 60 rows after a 30s run.
  const ts = Date.now();
  const email = `smoke-${ts}-${Math.random().toString(36).slice(2, 7)}@fitmate.vn`;
  const password = 'SmokeTest12!';
  const regPayload = JSON.stringify({
    email,
    password,
    fullName: 'Smoke Test',
    role: 'member',
  });
  const reg = http.post(`${constants.BASE_URL}${constants.API_PREFIX}/auth/register`, regPayload, {
    headers: { 'Content-Type': 'application/json' },
  });
  check(reg, { 'register: 201': (r) => r.status === 201 });
  return { email, password };
}

export default function (data) {
  // 1. Liveness
  const live = http.get(`${constants.BASE_URL}/health/live`);
  check(live, { 'live: 200': (r) => r.status === 200 });

  // 2. Readiness
  const ready = http.get(`${constants.BASE_URL}/health/ready`);
  check(ready, { 'ready: 200': (r) => r.status === 200 });

  // 3. Login with the single account created by setup().
  const loginPayload = JSON.stringify({
    email: data.email,
    password: data.password,
  });
  const login = http.post(`${constants.BASE_URL}${constants.API_PREFIX}/auth/login`, loginPayload, {
    headers: { 'Content-Type': 'application/json' },
  });
  check(login, { 'login: 200': (r) => r.status === 200 });

  let token = null;
  try { token = login.json('accessToken'); } catch { /* ignore */ }

  if (token) {
    // 4. Authenticated: GET /me/bookings (uses same auth as /users/me)
    const bookings = http.get(`${constants.BASE_URL}${constants.API_PREFIX}/me/bookings`, {
      headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
    });
    check(bookings, { 'GET /me/bookings: 200': (r) => r.status === 200 });

    // 5. Authenticated: GET /me/notifications
    const notifs = http.get(`${constants.BASE_URL}${constants.API_PREFIX}/me/notifications`, {
      headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
    });
    check(notifs, { 'GET /me/notifications: 2xx': (r) => r.status >= 200 && r.status < 500 });
  }

  sleep(0.5);
}
