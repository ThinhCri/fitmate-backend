# Load Test Plan & Report (skeleton)

> Load test chạy bằng [k6](https://k6.io) (đã chọn ở DEC-018). Mọi run ghi kết quả vào file `load-test-report.md` (cùng thư mục) cộng dồn.

## 1. Môi trường

- Local Docker Compose stack riêng (`compose.loadtest.yml`):
  - Postgres riêng (volume ephemeral, reset giữa run).
  - BE image đã build.
  - k6 runner (container).
- Không chạy trên production.
- Stub external (payment, email, AI) để không gọi provider thật.

## 2. Acceptance thresholds (đặt trước khi đo)

| Test | p50 | p95 | p99 | Error rate |
|---|---|---|---|---|
| Smoke | < 50 ms | < 200 ms | < 500 ms | < 0.1% |
| Normal | < 100 ms | < 300 ms | < 800 ms | < 0.5% |
| Peak | < 150 ms | < 500 ms | < 1.5 s | < 1% |
| Spike | < 300 ms | < 1 s | < 3 s | < 2% |
| Soak (1h) | < 150 ms | < 500 ms | < 1.5 s | < 1%, no memory leak |
| Stress | tìm giới hạn | tìm giới hạn | tìm giới hạn | ghi nhận saturation point |

Threshold này dựa trên `capacity-plan.md` và **không thay đổi** sau test để PASS.

## 3. Scripts

Lưu trong `tests/load/`:

- `smoke.js` — 1 VU, 30s. Health + 1 login + 1 list endpoint.
- `normal.js` — 20 VU, 5 phút. Mix read/write thực tế.
- `peak.js` — 100 VU, 10 phút. Booking + payment + social.
- `spike.js` — 0 → 200 VU trong 1 phút, giữ 2 phút, 0 → 200 lại.
- `soak.js` — 30 VU, 1 giờ. Tìm memory leak.
- `stress.js` — ramp tăng cho đến khi p95 > 2x target.

## 4. Kịch bản (scenarios)

Mỗi script mô phỏng:

- 60% member (đọc marketplace, list booking, log workout).
- 30% PT (xem booking, cập nhật availability).
- 5% admin (dashboard refresh).
- 5% booking cạnh tranh cùng slot (10 VU cùng đặt 1 PT slot → kiểm tra constraint unique).
- 10% request có Idempotency-Key.

## 5. Metrics thu thập

- k6:
  - `http_req_duration` (p50/p95/p99).
  - `http_req_failed`.
  - `vus`, `iterations`.
- App logs:
  - Slow query > 500ms.
  - DB connection pool saturation.
  - Outbox lag (nếu có job).
- Docker:
  - CPU, RAM, network.
- Postgres:
  - `pg_stat_statements` top queries.
  - Connection count.
  - Lock waits.

## 6. Cách chạy

```bash
# Smoke
docker compose -f compose.loadtest.yml run --rm k6 run /scripts/smoke.js

# Normal
docker compose -f compose.loadtest.yml run --rm k6 run /scripts/normal.js --duration 5m --vus 20
```

## 7. Cách ghi kết quả

Sau mỗi run, copy metrics vào bảng trong `load-test-report.md` cộng dồn. Format:

```
## Run <date> - <script> - <commit>
- BE commit: <sha>
- BE image digest: <sha256>
- Dataset seed: <seed version>
- Duration: <X min>
- VUs: <N>
- p50 / p95 / p99: <ms>
- Error rate: <%>
- DB CPU / RAM: <avg/max>
- Slow queries: <list>
- Verdict: PASS / FAIL / NEEDS_WORK
- Action: <follow-up>
```

## 8. Báo cáo bottleneck

Mỗi FAIL → phân tích:
- DB bound (slow query, missing index, lock)?
- App bound (CPU, GC, async)?
- IO bound (network, DB)?
- External (payment stub slow)?
- Memory leak (soak)?

Sửa → chạy lại script liên quan.

## 9. Trạng thái

- 2026-09-18: Skeleton. Scripts chưa viết. Report trống.
- Sau khi BE scaffold chạy được, sẽ viết scripts và run smoke trước.
