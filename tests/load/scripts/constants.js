// ─── Fitmate k6 load test — shared constants ───────────────────────────────────
// Default points at http://localhost:8088 (api-perf external port) or http://api-perf:8080 within docker network.

export const BASE_URL = __ENV.BASE_URL ?? 'http://localhost:8088';
export const API_PREFIX = '/api/v1';

// Fail closed so load scripts cannot be started accidentally by a normal local
// setup or aimed at staging/production. Every script imports this module either
// directly or through common.js, so the check runs before any HTTP request.
const LOAD_TEST_CONFIRMATION = 'FITMATE_LOCAL_ONLY';
const confirmation = __ENV.LOAD_TEST_CONFIRM ?? '';
if (confirmation !== LOAD_TEST_CONFIRMATION) {
  throw new Error(`Load test blocked. Set LOAD_TEST_CONFIRM=${LOAD_TEST_CONFIRMATION} explicitly.`);
}

const target = BASE_URL.match(/^https?:\/\/([^/:?#]+)/i);
const allowedHosts = ['localhost', '127.0.0.1', 'api', 'api-perf'];
if (!target || !allowedHosts.includes(target[1].toLowerCase())) {
  throw new Error(`Load test blocked for non-local target: ${BASE_URL}`);
}

// Seeded perf credentials (matches SeedPerfDataCommand)
export const PERF_PASSWORD = __ENV.PERF_PASSWORD ?? 'PerfTestPass123!';

export function getMemberEmail(index = 1) {
  const pad = String(index).padStart(3, '0');
  return `perf_member_${pad}@fitmate.vn`;
}

export function getPtEmail(index = 1) {
  const pad = String(index).padStart(3, '0');
  return `perf_pt_${pad}@fitmate.vn`;
}

// Fallback legacy constants
export const MEMBER_EMAIL = getMemberEmail(1);
export const MEMBER_PASSWORD = PERF_PASSWORD;
export const PT_EMAIL = getPtEmail(1);
export const PT_PASSWORD = PERF_PASSWORD;

// Admin (note: MFA challenge flow is not implemented in AuthController login flow)
export const ADMIN_EMAIL = __ENV.ADMIN_EMAIL ?? 'admin@fitmate.vn';
export const ADMIN_PASSWORD = __ENV.ADMIN_PASSWORD ?? 'PerfTestPass123!';

// API routes
export const ROUTES = {
  HEALTH_LIVE: '/health/live',
  HEALTH_READY: '/health/ready',
  AUTH_LOGIN: `${API_PREFIX}/auth/login`,
  AUTH_REGISTER: `${API_PREFIX}/auth/register`,
  USERS_ME: `${API_PREFIX}/users/me`,
  ME_BOOKINGS: `${API_PREFIX}/me/bookings`,
  TRAINERS: `${API_PREFIX}/trainers`,
  NOTIFICATIONS: `${API_PREFIX}/me/notifications`,
  PT_TRAINER_BOOKINGS: `${API_PREFIX}/me/trainer-bookings`,
  PT_REVENUE: `${API_PREFIX}/me/revenue`,
  PT_AVAILABILITY: `${API_PREFIX}/me/availability`,
  ADMIN_METRICS: `${API_PREFIX}/admin/metrics/overview`,
  ADMIN_USERS: `${API_PREFIX}/admin/users`,
  ADMIN_BOOKINGS: `${API_PREFIX}/admin/bookings`,
  ADMIN_FINANCE: `${API_PREFIX}/admin/finance/overview`,
  BOOKINGS_HOLDS: `${API_PREFIX}/bookings/holds`,
};
