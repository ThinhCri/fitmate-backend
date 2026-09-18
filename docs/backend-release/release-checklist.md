# Release Checklist

> Gate trước khi release. Mỗi gate có trạng thái: PASS / FAIL / NOT_RUN / BLOCKED. Không gán PASS nếu không có bằng chứng.

## Gate matrix

| # | Gate | Trạng thái | Bằng chứng / command |
|---|---|---|---|
| 1 | Local reproducible | NOT_RUN | — |
| 2 | BE functional | NOT_RUN | — |
| 3 | Android integration-ready | NOT_RUN | — |
| 4 | Admin integration-ready | NOT_RUN | — |
| 5 | CI verified | NOT_RUN | — |
| 6 | Staging deployed/tested | NOT_RUN | — |
| 7 | Load targets measured | NOT_RUN | — |
| 8 | Backup/restore verified | NOT_RUN | — |
| 9 | Payment production-ready | NOT_RUN | — |
| 10a | AI integration-ready | NOT_RUN | — |
| 10b | AI thực tế deployed | NOT_RUN | — |
| 11 | Production release-ready | NOT_RUN | — |
| 12 | Google Play submission prerequisites | NOT_RUN | — |

## Chi tiết

### 1. Local reproducible

- [ ] `docker compose up` chạy được từ clean.
- [ ] Migration tự chạy qua profile `migrate`.
- [ ] Seed demo tạo được qua `dotnet run -- seed`.
- [ ] Login demo user hoạt động.
- [ ] Curl `/health/ready` trả 200.

### 2. BE functional

- [ ] Tất cả endpoint trong OpenAPI có implementation.
- [ ] Unit tests pass: `dotnet test`.
- [ ] Integration tests pass (DB).
- [ ] Contract tests pass (snapshot vs OpenAPI).
- [ ] Auth tests pass (401, 403, ownership).
- [ ] Idempotency tests pass (retry, conflict).
- [ ] Concurrency tests pass (booking conflict).
- [ ] Money tests pass (rounding, sum cap).
- [ ] Timezone tests pass.
- [ ] Upload tests pass (size, type).
- [ ] AI adapter tests pass (mock provider: success, no result, timeout, invalid output).

### 3. Android integration-ready

- [ ] Mobile app build với base URL staging OK.
- [ ] Auth flow end-to-end: register / login / refresh / logout.
- [ ] Marketplace list + filter.
- [ ] Booking hold + payment + confirm + complete.
- [ ] Cancel + refund (qua admin).
- [ ] Workout log + session.
- [ ] Meal log + scan (AI unavailable fallback).
- [ ] Social post + comment + react.
- [ ] Chat friend + AI fallback.
- [ ] Notification list + read.

### 4. Admin integration-ready

- [ ] Admin login + session.
- [ ] Overview dashboard load đúng số liệu.
- [ ] User list + lock/unlock.
- [ ] PT list + certificate approve/reject.
- [ ] Booking list + force refund.
- [ ] Finance overview + ledger + PT earnings.
- [ ] Reports list + resolve/dismiss.
- [ ] Audit log.
- [ ] Settings đổi password admin.

### 5. CI verified

- [ ] BE CI: install lockfile, lint/typecheck, build, unit/integration/contract tests, Docker build, container smoke.
- [ ] Web CI: install, lint/typecheck, production build.
- [ ] Mobile CI: install, lint/typecheck, type-check, test.
- [ ] Secret scanning pass.
- [ ] Container scanning pass.

### 6. Staging deployed/tested

- [ ] Staging compose up.
- [ ] Migration chạy thành công.
- [ ] Health check pass.
- [ ] Smoke script pass.
- [ ] Logs structured.
- [ ] Metrics endpoint expose.

### 7. Load targets measured

- [ ] Smoke load pass.
- [ ] Normal load pass.
- [ ] Peak load pass.
- [ ] Spike pass.
- [ ] Soak pass (no leak).
- [ ] Stress — ghi saturation point.
- [ ] Booking concurrency test pass.
- [ ] DB connection pool không saturate.

### 8. Backup/restore verified

- [ ] Backup script chạy nightly.
- [ ] Restore test trong 90 ngày gần nhất có ghi nhận RPO/RTO.
- [ ] Off-VPS storage verify.

### 9. Payment production-ready

- [ ] VNPay merchant account verified.
- [ ] Sandbox E2E pass.
- [ ] Webhook verify HMAC + idempotency.
- [ ] Reconciliation job chạy mỗi giờ.
- [ ] Refund flow test.
- [ ] Alert cho mismatch.

### 10a. AI integration-ready

- [ ] AI adapter trả `AI_UNAVAILABLE` đúng khi provider down.
- [ ] Schema validation pass.
- [ ] Timeout / retry / circuit breaker theo `ai-contract.md`.
- [ ] Rate limit theo user.

### 10b. AI thực tế deployed

- [ ] Nhóm AI bàn giao theo `ai-handoff-checklist.md`.
- [ ] Run trong môi trường cô lập.
- [ ] Đo tài nguyên / latency.
- [ ] Quyết định deployment (in-process / sidecar / service riêng).
- [ ] Production deploy.

### 11. Production release-ready

- [ ] Tất cả gate trên PASS.
- [ ] User confirm release window.
- [ ] Backup ngay trước deploy.
- [ ] Migration safety reviewed.
- [ ] Release record ghi (commit, digest, version, time).
- [ ] Monitor 30 phút đầu.

### 12. Google Play submission prerequisites

- [ ] Application ID đã đặt.
- [ ] Keystore + upload key generated và lưu secret manager.
- [ ] Play Console account có quyền upload.
- [ ] Service account JSON (cho CI auto upload internal-testing).
- [ ] AAB release build OK.
- [ ] Store listing (text, icon, screenshots) sẵn.
- [ ] Privacy policy URL.
- [ ] Data Safety form completed.
- [ ] Account deletion flow verified end-to-end.
- [ ] Health declaration (nếu app có health features).
- [ ] Target SDK mới nhất (theo Google policy hiện hành).
- [ ] Permissions giảm thiểu.
- [ ] Permissions policy cho location/health nếu có.
