import http from 'k6/http';
import { check, group } from 'k6';
import * as constants from './constants.js';

// ─── Auth helpers ──────────────────────────────────────────────────────────────

/**
 * Login member and return { accessToken, userId, expiresAt }.
 * Caches per VU to avoid re-authenticating every iteration.
 */
let _memberTokenCache = null;

export function getMemberToken(baseUrl) {
  if (_memberTokenCache) return _memberTokenCache;

  const payload = JSON.stringify({
    email: constants.MEMBER_EMAIL,
    password: constants.MEMBER_PASSWORD,
  });

  const res = http.post(`${baseUrl}${constants.API_PREFIX}/auth/login`, payload, {
    headers: { 'Content-Type': 'application/json' },
  });

  check(res, {
    'member login: status 200': (r) => r.status === 200,
    'member login: has accessToken': (r) => {
      try {
        const body = r.json();
        return body && body.accessToken !== undefined;
      } catch {
        return false;
      }
    },
  });

  const body = res.json();
  _memberTokenCache = {
    accessToken: body.accessToken,
    userId: body.user?.id ?? '',
    expiresAt: Date.now() + (body.expiresIn ?? 3600) * 1000,
  };
  return _memberTokenCache;
}

/**
 * Login PT and return { accessToken, userId }.
 */
let _ptTokenCache = null;

export function getPtToken(baseUrl) {
  if (_ptTokenCache) return _ptTokenCache;

  const payload = JSON.stringify({
    email: constants.PT_EMAIL,
    password: constants.PT_PASSWORD,
  });

  const res = http.post(`${baseUrl}${constants.API_PREFIX}/auth/login`, payload, {
    headers: { 'Content-Type': 'application/json' },
  });

  check(res, {
    'PT login: status 200': (r) => r.status === 200,
    'PT login: has accessToken': (r) => {
      try {
        const body = r.json();
        return body && body.accessToken !== undefined;
      } catch {
        return false;
      }
    },
  });

  const body = res.json();
  _ptTokenCache = {
    accessToken: body.accessToken,
    userId: body.user?.id ?? '',
    expiresAt: Date.now() + (body.expiresIn ?? 3600) * 1000,
  };
  return _ptTokenCache;
}

/**
 * Login admin and return { accessToken, userId }.
 */
let _adminTokenCache = null;

export function getAdminToken(baseUrl) {
  if (_adminTokenCache) return _adminTokenCache;

  const payload = JSON.stringify({
    email: constants.ADMIN_EMAIL,
    password: constants.ADMIN_PASSWORD,
  });

  const res = http.post(`${baseUrl}${constants.API_PREFIX}/auth/login`, payload, {
    headers: { 'Content-Type': 'application/json' },
  });

  check(res, {
    'admin login: status 200': (r) => r.status === 200,
    'admin login: has accessToken': (r) => {
      try {
        const body = r.json();
        return body && body.accessToken !== undefined;
      } catch {
        return false;
      }
    },
  });

  const body = res.json();
  _adminTokenCache = {
    accessToken: body.accessToken,
    userId: body.user?.id ?? '',
    expiresAt: Date.now() + (body.expiresIn ?? 3600) * 1000,
  };
  return _adminTokenCache;
}

// ─── Request helpers ──────────────────────────────────────────────────────────

/**
 * Authenticated GET with API prefix and standard headers.
 */
export function authGet(baseUrl, path, token) {
  const res = http.get(`${baseUrl}${constants.API_PREFIX}${path}`, {
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: 'application/json',
    },
  });
  return res;
}

/**
 * Authenticated POST with API prefix and standard headers.
 */
export function authPost(baseUrl, path, body, token, extraHeaders = {}) {
  const payload = typeof body === 'string' ? body : JSON.stringify(body);
  const headers = {
    'Content-Type': 'application/json',
    Accept: 'application/json',
    Authorization: `Bearer ${token}`,
    ...extraHeaders,
  };
  const res = http.post(`${baseUrl}${constants.API_PREFIX}${path}`, payload, { headers });
  return res;
}

/**
 * Public (unauthenticated) request — bypasses API_PREFIX only when explicitly given a full path.
 * Use for /health/* and any non-prefixed endpoints.
 */
export function publicRequest(method, url, body, extraHeaders = {}) {
  const payload = body === undefined || body === null ? null : (typeof body === 'string' ? body : JSON.stringify(body));
  const headers = { 'Content-Type': 'application/json', ...extraHeaders };
  return http.request(method, url, payload, { headers });
}

// ─── Health checks ────────────────────────────────────────────────────────────

export function checkHealth(baseUrl) {
  group('Health', () => {
    const live = http.get(`${baseUrl}/health/live`);
    check(live, {
      'liveness: 200': (r) => r.status === 200,
    });

    const ready = http.get(`${baseUrl}/health/ready`);
    check(ready, {
      'readiness: 200': (r) => r.status === 200,
    });
  });
}

// ─── Token-cache reset (per VU iteration) ────────────────────────────────────

export function resetCaches() {
  if (_memberTokenCache && Date.now() > _memberTokenCache.expiresAt - 60_000) {
    _memberTokenCache = null;
  }
  if (_ptTokenCache && Date.now() > (_ptTokenCache.expiresAt ?? Infinity) - 60_000) {
    _ptTokenCache = null;
  }
  if (_adminTokenCache && Date.now() > (_adminTokenCache.expiresAt ?? Infinity) - 60_000) {
    _adminTokenCache = null;
  }
}
