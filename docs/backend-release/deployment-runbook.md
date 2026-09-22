# Deployment Runbook

> Baseline update 2026-09-22: tài liệu này giữ chi tiết/lịch sử; khi có mâu thuẫn, dùng [README](README.md), [domain rules](domain-rules.md), [tasks](tasks.md) và DEC-051. Không dùng giá/quota, trạng thái DONE, mock adapter hoặc route giả định trong tài liệu cũ làm bằng chứng production. Nguồn policy cập nhật: [release sources](policy-and-runtime-sources.md).

> Triển khai BE lên môi trường staging/production bằng Docker Compose trên 1 VPS. Chưa tự deploy production.

## 1. Topology

```
[ Internet ]
   │
   ▼
[ Reverse proxy ] (Caddy / Nginx + Let's Encrypt)
   │
   ├──► admin.fitmate.vn       → static admin web (sau khi build)
   ├──► api.fitmate.vn         → be container (port 8080)
   └──► /api/v1/payments/vnpay/ipn (allow VNPay IPN)
[ Postgres 16 ]   [ MailHog/SMTP ]   [ Object storage ] (sau khi cần)
```

## 2. Yêu cầu hạ tầng (user cần cấp)

- VPS Linux (Ubuntu 22.04+ hoặc Debian 12+) — 2 vCPU, 4 GB RAM minimum cho staging.
- Domain + DNS trỏ về VPS.
- TLS certificate qua Let's Encrypt.
- Backup volume (off-VPS) cho Postgres.
- SMTP provider (SendGrid, Mailgun, AWS SES) hoặc self-host Postfix (khuyến nghị dùng provider).
- Log aggregation (Loki / CloudWatch / syslog).

## 3. Chuẩn bị repo (một lần)

1. Tạo Git remote cho 3 repo (FE, BE, Web) trên Git hosting (GitHub / GitLab).
2. Cấu hình GitHub Actions / GitLab CI (xem `ci.md`).
3. Cấu hình registry container (Docker Hub / GHCR / GitLab registry).
4. Cấp OIDC cho CI push image (không dùng long-lived docker password).

## 4. Staging

### 4.1 Trigger

- Push lên branch `main` của BE repo.
- Hoặc manual trigger từ CI UI.

### 4.2 Steps

1. CI build image với tag `<short-sha>` + digest.
2. Push lên registry.
3. SSH tới staging VPS (hoặc dùng deploy key qua CI).
4. Pull image mới.
5. Chạy `docker compose --profile migrate up migrate` (EF Core database update).
6. Restart `be` container với image mới.
7. Health check `/health/ready`.
8. Smoke test:
   - `curl /health/live`
   - `curl /health/ready`
   - Login demo user qua API.
   - 1 list endpoint.
9. Ghi kết quả + commit SHA + image digest vào `release-checklist.md`.

### 4.3 Rollback

- CI giữ image 5 version gần nhất.
- Rollback: `docker compose up -d --force-recreate be` với tag cũ.
- DB rollback: chỉ khi migration an toàn (`Down` đã chạy được và chưa có data mới). Thường thì rollback image, giữ DB.

## 5. Production (chỉ chuẩn bị pipeline, chưa tự deploy)

### 5.1 Trigger

- Tag `vX.Y.Z` trên BE repo.
- Manual approval trong CI/CD (protected environment).
- Cần user/operator duyệt.

### 5.2 Steps (tương tự staging +)

1. CI build + scan image (trivy / docker scout).
2. Push image với tag `X.Y.Z` + digest.
3. Backup DB trước khi deploy.
4. Chạy migration.
5. Blue/green hoặc recreate với health check (Compose đơn giản → recreate).
6. Smoke test tự động + manual checklist.
7. Ghi release record (commit, image digest, migration version, deploy time).
8. Monitor 30 phút đầu.

### 5.3 Migration safety

- Migration destructive cần:
  - Backup verified.
  - Tested trên staging với data shape production.
  - Có rollback plan (cũng destructive ngược).
- Không chạy migration ngay trước peak hour.

## 6. Health checks

- `/health/live`: process sống. 200 OK.
- `/health/ready`: DB connect OK + config loaded + optional dependency (Redis nếu có). 503 nếu fail.
- CI/CD dùng `/health/ready` để gate traffic.

## 7. Logs

- App logs → stdout/stderr → Docker logging driver.
- Forward tới Loki / CloudWatch / Datadog.
- Retention: 30 ngày cho app log, 1 năm cho audit.

## 8. Metrics

- Prometheus scrape `/metrics` (Prometheus-net.AspNetCore).
- Scrape interval 15s.
- Dashboard: request rate, p50/p95/p99, error rate, DB pool, GC, AI latency.

## 9. Backups

- Postgres: `pg_dump` nightly. Lưu off-VPS. Retention 30 ngày.
- Object storage (khi có): cross-region replication.
- Audit log: archive monthly.

## 10. Disaster recovery

- RPO: ≤ 24 giờ (chấp nhận mất 1 ngày DB).
- RTO: ≤ 2 giờ (restore DB + redeploy).
- Test restore mỗi quý (xem `runbooks/db-failover.md`).

## 11. Trạng thái

- 2026-09-18: Skeleton. Chưa có VPS / domain.
