import http from 'k6/http';
import { check, group } from 'k6';
import * as constants from './constants.js';

// ─── Auth helpers ──────────────────────────────────────────────────────────────

const _tokenCache = {};

/**
 * Login member and return { accessToken, userId, expiresAt }.
 * Allocates 1-1 account per VU from the pool of 100 members.
 */
export function getMemberToken(baseUrl, vuId) {
  const effectiveVuId = vuId || __VU || 1;
  const memberIndex = ((effectiveVuId - 1) % 100) + 1;
  const email = constants.getMemberEmail(memberIndex);

  if (_tokenCache[email] && Date.now() < _tokenCache[email].expiresAt - 60_000) {
    return _tokenCache[email];
  }

  const payload = JSON.stringify({
    email,
    password: constants.PERF_PASSWORD,
  });

  const res = http.post(`${baseUrl}${constants.ROUTES.AUTH_LOGIN}`, payload, {
    headers: { 'Content-Type': 'application/json' },
    tags: { type: 'auth' },
  });

  const loginSuccess = check(res, {
    'member login: status 200': (r) => r.status === 200,
    'member login: has accessToken': (r) => {
      try {
        const body = r.json();
        return body && Boolean(body.accessToken);
      } catch {
        return false;
      }
    },
  });

  if (!loginSuccess) {
    return null;
  }

  const body = res.json();
  const session = {
    accessToken: body.accessToken,
    userId: body.user?.id ?? '',
    expiresAt: Date.now() + (body.expiresIn ?? 3600) * 1000,
  };
  _tokenCache[email] = session;
  return session;
}

/**
 * Login PT and return { accessToken, userId, expiresAt }.
 * Allocates 1-1 account per VU from the pool of 20 PTs.
 */
export function getPtToken(baseUrl, vuId) {
  const effectiveVuId = vuId || __VU || 1;
  const ptIndex = ((effectiveVuId - 1) % 20) + 1;
  const email = constants.getPtEmail(ptIndex);

  if (_tokenCache[email] && Date.now() < _tokenCache[email].expiresAt - 60_000) {
    return _tokenCache[email];
  }

  const payload = JSON.stringify({
    email,
    password: constants.PERF_PASSWORD,
  });

  const res = http.post(`${baseUrl}${constants.ROUTES.AUTH_LOGIN}`, payload, {
    headers: { 'Content-Type': 'application/json' },
    tags: { type: 'auth' },
  });

  const loginSuccess = check(res, {
    'PT login: status 200': (r) => r.status === 200,
    'PT login: has accessToken': (r) => {
      try {
        const body = r.json();
        return body && Boolean(body.accessToken);
      } catch {
        return false;
      }
    },
  });

  if (!loginSuccess) {
    return null;
  }

  const body = res.json();
  const session = {
    accessToken: body.accessToken,
    userId: body.user?.id ?? '',
    expiresAt: Date.now() + (body.expiresIn ?? 3600) * 1000,
  };
  _tokenCache[email] = session;
  return session;
}

/**
 * Login admin and return { accessToken, userId } or BLOCKED state.
 * If AuthController lacks 2FA challenge support or admin is not seeded, records BLOCKED status.
 */
export function getAdminToken(baseUrl) {
  const email = constants.ADMIN_EMAIL;
  if (_tokenCache[email] && Date.now() < _tokenCache[email].expiresAt - 60_000) {
    return _tokenCache[email];
  }

  const payload = JSON.stringify({
    email,
    password: constants.ADMIN_PASSWORD,
  });

  const res = http.post(`${baseUrl}${constants.ROUTES.AUTH_LOGIN}`, payload, {
    headers: { 'Content-Type': 'application/json' },
    tags: { type: 'auth' },
  });

  if (res.status !== 200) {
    // Admin login scenario is blocked due to missing MFA challenge handler during login flow
    return {
      blocked: true,
      reason: 'Admin scenario BLOCKED: /api/v1/auth/login lacks MFA challenge handling in AuthController contract',
      accessToken: null,
    };
  }

  try {
    const body = res.json();
    if (!body || !body.accessToken) {
      return {
        blocked: true,
        reason: 'Admin login response missing accessToken',
        accessToken: null,
      };
    }

    const session = {
      blocked: false,
      accessToken: body.accessToken,
      userId: body.user?.id ?? '',
      expiresAt: Date.now() + (body.expiresIn ?? 3600) * 1000,
    };
    _tokenCache[email] = session;
    return session;
  } catch {
    return {
      blocked: true,
      reason: 'Admin login response JSON parse error',
      accessToken: null,
    };
  }
}

// ─── Request helpers ──────────────────────────────────────────────────────────

/**
 * Authenticated GET with standard headers and tags.
 */
export function authGet(baseUrl, path, token, extraTags = {}) {
  const headers = {
    Accept: 'application/json',
  };
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const tags = { type: 'read', ...extraTags };
  const res = http.get(`${baseUrl}${constants.API_PREFIX}${path}`, {
    headers,
    tags,
  });
  return res;
}

/**
 * Authenticated POST with standard headers and tags.
 */
export function authPost(baseUrl, path, body, token, extraHeaders = {}, extraTags = {}) {
  const payload = typeof body === 'string' ? body : JSON.stringify(body);
  const headers = {
    'Content-Type': 'application/json',
    Accept: 'application/json',
    ...extraHeaders,
  };
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const tags = { type: 'write', ...extraTags };
  const res = http.post(`${baseUrl}${constants.API_PREFIX}${path}`, payload, {
    headers,
    tags,
  });
  return res;
}

/**
 * Public (unauthenticated) request.
 */
export function publicRequest(method, url, body, extraHeaders = {}, extraTags = {}) {
  const payload = body === undefined || body === null ? null : (typeof body === 'string' ? body : JSON.stringify(body));
  const headers = { 'Content-Type': 'application/json', ...extraHeaders };
  const tags = { type: 'public', ...extraTags };
  return http.request(method, url, payload, { headers, tags });
}

// ─── Health checks ────────────────────────────────────────────────────────────

export function checkHealth(baseUrl) {
  group('Health', () => {
    const live = http.get(`${baseUrl}${constants.ROUTES.HEALTH_LIVE}`, {
      tags: { type: 'health' },
    });
    check(live, {
      'liveness: 200': (r) => r.status === 200,
    });

    const ready = http.get(`${baseUrl}${constants.ROUTES.HEALTH_READY}`, {
      tags: { type: 'health' },
    });
    check(ready, {
      'readiness: 200': (r) => r.status === 200,
    });
  });
}

// ─── Token-cache reset ────────────────────────────────────────────────────────

/**
 * Cleans up expired cached tokens (only when remaining lifetime < 60s).
 */
export function resetCaches() {
  const now = Date.now();
  for (const email of Object.keys(_tokenCache)) {
    if (_tokenCache[email] && now > _tokenCache[email].expiresAt - 60_000) {
      delete _tokenCache[email];
    }
  }
}
