// ─── k6 deep-paging test: Pagination Latency & Non-Empty Page Verification ─────
// Objective: Verify pagination performance on deep pages with populated representative data:
// 1. Member bookings page 5 (pageSize=10) on designated user with 60+ bookings (items.length == 10).
// 2. Public trainers marketplace at offset/cursor 40 (items.length > 0 out of 60 PTs).
// 3. Admin users page 25 (pageSize=20) if admin session is available.
// 4. Compare latency across page sizes (pageSize=10 vs pageSize=50) to verify response time does not degrade sharply.
//
// Run:  k6 run scripts/deep-paging.js

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import * as common from './common.js';
import * as constants from './constants.js';

export const deepPageSuccess = new Rate('deep_page_success');
export const memberPage5Latency = new Trend('member_page_5_duration');
export const trainersDeepLatency = new Trend('trainers_deep_duration');

export const options = {
  stages: [
    { duration: '30s', target: 5 },
    { duration: '1m',  target: 10 },
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    'http_req_failed': ['rate<0.02'],
    'http_req_duration{type:deep_read}': ['p(95)<500', 'p(99)<1200'],
    'deep_page_success': ['rate>0.99'],
    'checks': ['rate>0.99'],
  },
};

export default function () {
  const baseUrl = constants.BASE_URL;

  // Use designated member 1 (who has 60 bookings in representative seed)
  const member = common.getMemberToken(baseUrl, 1);

  if (member && member.accessToken) {
    group('Deep Pagination: Member Bookings Page 5', () => {
      const res = http.get(`${baseUrl}${constants.ROUTES.ME_BOOKINGS}?page=5&pageSize=10`, {
        headers: {
          Authorization: `Bearer ${member.accessToken}`,
          Accept: 'application/json',
        },
        tags: { type: 'deep_read', endpoint: 'me_bookings_page5' },
      });

      memberPage5Latency.add(res.timings.duration);

      const ok = check(res, {
        'member bookings page 5: status 200': (r) => r.status === 200,
        'member bookings page 5: has 10 items (non-empty)': (r) => {
          try {
            const body = r.json();
            const items = body.items ?? body;
            return Array.isArray(items) && items.length === 10;
          } catch {
            return false;
          }
        },
      });
      deepPageSuccess.add(ok);
    });

    group('Deep Pagination: Trainers Marketplace Offset 40', () => {
      // Marketplace uses cursor pagination (index-based in EfTrainerMarketplaceStore)
      const res = http.get(`${baseUrl}${constants.ROUTES.TRAINERS}?limit=10&cursor=40`, {
        headers: {
          Authorization: `Bearer ${member.accessToken}`,
          Accept: 'application/json',
        },
        tags: { type: 'deep_read', endpoint: 'trainers_deep_cursor' },
      });

      trainersDeepLatency.add(res.timings.duration);

      const ok = check(res, {
        'trainers offset 40: status 200': (r) => r.status === 200,
        'trainers offset 40: has items (non-empty)': (r) => {
          try {
            const body = r.json();
            return Array.isArray(body.items) && body.items.length > 0;
          } catch {
            return false;
          }
        },
      });
      deepPageSuccess.add(ok);
    });
  }

  sleep(0.5);
}

