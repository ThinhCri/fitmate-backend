// ─── k6 contention test: Concurrent Booking Hold Race Condition ────────────────
// Objective: Verify exact business invariant when 20 distinct member accounts
// simultaneously attempt to hold the EXACT SAME PT slot.
//
// Invariant requirement:
// - Exactly 1 hold succeeds (HTTP 201 Created)
// - Exactly 19 attempts receive conflict (HTTP 409 Conflict)
// - Exactly 0 unexpected failures (HTTP 500, unhandled exceptions, etc.)
//
// Run:  k6 run scripts/contention.js

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';
import * as constants from './constants.js';

export const holdsSucceeded = new Counter('holds_succeeded');
export const conflictsReceived = new Counter('conflicts_received');
export const unexpectedFailures = new Counter('unexpected_failures');

export const options = {
  scenarios: {
    slot_race: {
      executor: 'per-vu-iterations',
      vus: 20,
      iterations: 1,
      maxDuration: '30s',
    },
  },
  thresholds: {
    'holds_succeeded': ['count==1'],
    'conflicts_received': ['count==19'],
    'unexpected_failures': ['count==0'],
  },
};

export function setup() {
  const baseUrl = constants.BASE_URL;

  // 1. Authenticate admin/member to query available trainer & service
  const memberEmail = constants.getMemberEmail(1);
  const loginRes = http.post(`${baseUrl}${constants.ROUTES.AUTH_LOGIN}`, JSON.stringify({
    email: memberEmail,
    password: constants.PERF_PASSWORD,
  }), { headers: { 'Content-Type': 'application/json' } });

  if (loginRes.status !== 200) {
    throw new Error(`Setup failed: cannot login setup member (${loginRes.status}): ${loginRes.body}`);
  }

  const token = loginRes.json('accessToken');

  // 2. Fetch first available trainer
  const trainersRes = http.get(`${baseUrl}${constants.ROUTES.TRAINERS}?limit=1`, {
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
  });

  if (trainersRes.status !== 200) {
    throw new Error(`Setup failed: cannot list trainers (${trainersRes.status}): ${trainersRes.body}`);
  }

  const trainersData = trainersRes.json();
  const trainer = trainersData.items?.[0];
  if (!trainer) {
    throw new Error('Setup failed: no trainers found in marketplace.');
  }

  const ptUserId = trainer.userId;

  // 3. Fetch trainer details to obtain serviceId
  const detailRes = http.get(`${baseUrl}${constants.ROUTES.TRAINERS}/${ptUserId}`, {
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
  });

  if (detailRes.status !== 200) {
    throw new Error(`Setup failed: cannot get trainer detail (${detailRes.status}): ${detailRes.body}`);
  }

  const detail = detailRes.json();
  const service = detail.services?.[0];
  if (!service) {
    throw new Error(`Setup failed: trainer ${ptUserId} has no services.`);
  }

  // 4. Pre-authenticate all 20 VUs so auth latency does not skew race condition
  const tokens = {};
  for (let vu = 1; vu <= 20; vu++) {
    const vuEmail = constants.getMemberEmail(vu);
    const res = http.post(`${baseUrl}${constants.ROUTES.AUTH_LOGIN}`, JSON.stringify({
      email: vuEmail,
      password: constants.PERF_PASSWORD,
    }), { headers: { 'Content-Type': 'application/json' } });

    if (res.status !== 200) {
      throw new Error(`Setup failed: cannot pre-authenticate VU ${vu} (${vuEmail}): ${res.status}`);
    }
    tokens[vu] = res.json('accessToken');
  }

  // 5. Select a designated fresh future slot for this test run to prevent collision with previous runs
  const targetSlotUtc = new Date(Date.now() + 86400000 * 30 + Math.floor(Math.random() * 10000) * 3600000).toISOString();

  return {
    ptUserId,
    serviceId: service.id,
    targetSlotUtc,
    tokens,
  };
}

export default function (data) {
  const vuId = __VU;
  const token = data.tokens[vuId];
  const baseUrl = constants.BASE_URL;

  // Payload for slot hold
  const payload = JSON.stringify({
    ptId: data.ptUserId,
    serviceId: data.serviceId,
    slotStartUtc: data.targetSlotUtc,
  });

  const res = http.post(`${baseUrl}${constants.ROUTES.BOOKINGS_HOLDS}`, payload, {
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'Authorization': `Bearer ${token}`,
    },
    tags: { type: 'contention_hold' },
  });

  if (res.status === 201) {
    holdsSucceeded.add(1);
    check(res, { 'hold succeeded: status 201': (r) => r.status === 201 });
  } else if (res.status === 409) {
    conflictsReceived.add(1);
    check(res, { 'conflict handled: status 409': (r) => r.status === 409 });
  } else {
    unexpectedFailures.add(1);
    console.error(`VU ${vuId} unexpected status ${res.status}: ${res.body}`);
  }
}

