# User Actions & AI Handoff

> Việc user + nhóm AI phải làm. Mỗi mục có: ID, việc, lý do, bắt buộc/tùy chọn, thời điểm, hướng dẫn, cách verify, task bị chặn.

## UA-001 — Rotate Supabase password `doanxemtoilaai`

- **Lý do**: Đã commit vào `application.properties` của BE Spring Boot cũ. Có khả năng bị lộ.
- **Bắt buộc**: ✅
- **Thời điểm**: Ngay khi có thể.
- **Hướng dẫn**: Supabase dashboard → Settings → Database → Reset password. Sau đó cập nhật connection string.
- **Verify**: Test connect với password mới fail với password cũ.
- **Task bị chặn**: Việc xóa secret khỏi Git history (BE-099).

## UA-002 — Cấp Git hosting cho 3 repo (nếu chưa có public)

- **Lý do**: Cần cho CI/CD và collaboration.
- **Bắt buộc**: ✅ (trước khi CI chạy).
- **Hướng dẫn**: GitHub / GitLab. Thêm collaborator nếu cần.
- **Verify**: Push/pull thành công.

## UA-003 — Cấp OIDC cho CI (GitHub Actions) hoặc registry credentials

- **Lý do**: CI cần push Docker image.
- **Bắt buộc**: ✅ (trước khi CI build image).
- **Hướng dẫn**: GitHub Actions → Settings → Secrets → OIDC token exchange. Hoặc GitLab CI → Variables.
- **Verify**: CI login registry thành công.

## UA-004 — Cấp VPS cho staging

- **Lý do**: Cần chạy Docker Compose.
- **Bắt buộc**: ✅ (trước khi deploy staging).
- **Hướng dẫn**: VPS Ubuntu 22.04+ / Debian 12+, 2 vCPU, 4 GB RAM. Cấu hình SSH key.
- **Verify**: SSH vào được, docker compose up OK.

## UA-005 — Cấp domain + DNS

- **Lý do**: Cần TLS + origin cho production.
- **Bắt buộc**: ✅ (trước khi production).
- **Hướng dẫn**: `admin.fitmate.vn`, `api.fitmate.vn`, `staging-api.fitmate.vn`. Trỏ về VPS IP.
- **Verify**: `dig` resolve đúng.

## UA-006 — Cấp TLS certificate

- **Lý do**: HTTPS bắt buộc.
- **Bắt buộc**: ✅.
- **Hướng dẫn**: Caddy tự động qua Let's Encrypt.
- **Verify**: `https://...` OK.

## UA-007 — Cấp SMTP (email) credentials

- **Lý do**: Cần gửi email xác nhận / reset password.
- **Bắt buộc**: ✅ (trước khi production).
- **Hướng dẫn**: SendGrid / Mailgun / AWS SES. Cấu hình SPF/DKIM.
- **Verify**: Gửi test email tới address thật.

## UA-008 — Cấp VNPay merchant (khi đến giai đoạn payment)

- **Lý do**: Tích hợp payment thật.
- **Bắt buộc**: ✅ (trước khi payment production).
- **Hướng dẫn**: Đăng ký merchant VNPay, lấy TMN, hash secret, return URL, IPN URL.
- **Verify**: Test với sandbox merchant, sau đó production.

## UA-009 — Cấp Android keystore + upload key

- **Lý do**: Release AAB cần signing.
- **Bắt buộc**: ✅ (trước khi release Android).
- **Hướng dẫn**: Tạo keystore qua Android Studio. Upload key qua Play Console → Setup → App signing.
- **Verify**: Build AAB signed thành công, upload test track OK.

## UA-010 — Tạo Play Console account + service account

- **Lý do**: Auto upload internal-testing.
- **Bắt buộc**: ✅ (trước khi auto upload).
- **Hướng dẫn**: Google Cloud Console → Service Account với role Play Android Developer. Tải JSON key.
- **Verify**: `bundletool` hoặc CI upload thành công.

## UA-011 — Chốt các quyết định nghiệp vụ production

- **Lý do**: Nhiều BR chưa chốt (xem `domain-rules.md §9`).
- **Bắt buộc**: ✅ (trước khi payment production).
- **Hướng dẫn**: Đọc `domain-rules.md §9`, điền vào `decisions.md` dạng DEC-NNN.
- **Verify**: Tất cả item ✅ chốt.

## UA-012 — Privacy policy + Terms of Service

- **Lý do**: Google Play + người dùng cần.
- **Bắt buộc**: ✅.
- **Hướng dẫn**: Soạn bởi legal team. Host trên domain Fitmate.
- **Verify**: URL accessible, link trong app.

## UA-013 — Data Safety form cho Play

- **Lý do**: Google Play yêu cầu.
- **Bắt buộc**: ✅.
- **Hướng dẫn**: Tự khai trên Play Console → Data Safety. Dựa trên `domain-rules.md §5`.
- **Verify**: Form submit OK.

## UA-014 — Account deletion flow verified

- **Lý do**: Google Play yêu cầu cho app có tài khoản.
- **Bắt buộc**: ✅.
- **Hướng dẫn**: `DELETE /users/me` đã có trong OpenAPI. Test end-to-end.
- **Verify**: User xóa thành công, không login lại được.

## UA-015 — Backup off-VPS storage

- **Lý do**: Disaster recovery.
- **Bắt buộc**: ✅.
- **Hướng dẫn**: S3-compatible (Backblaze B2 / DO Spaces / MinIO). Cấu hình `pg_dump` cron upload.
- **Verify**: Restore test từ backup thật.

## UA-016 — Monitoring service

- **Lý do**: Cần alert khi lỗi.
- **Bắt buộc**: ✅.
- **Hướng dẫn**: Uptime Kuma (free, self-host) hoặc Grafana Cloud / Datadog.
- **Verify**: Alert gửi khi `/health/ready` fail.

## UA-017 — Support channel

- **Lý do**: User cần liên hệ.
- **Bắt buộc**: ✅.
- **Hướng dẫn**: Email support@fitmate.vn hoặc form. Liên kết trong app + listing.
- **Verify**: Email nhận được, response trong 24h.

## AI Team — Handoff

Khi nhóm AI bàn giao, cung cấp đầy đủ mục trong `ai-handoff-checklist.md`. Nếu thiếu, dừng nhận.

## UA-018 — Chốt AppointmentStatus enum giữa 3 client

- **Lý do**: Mobile có 6 states (`pending|confirmed|completed|cancelled|late|rescheduled`), web admin có 4 (`in_progress|completed|pending|cancelled`), BE OpenAPI có 7 (`Held|AwaitingPayment|Paid|Completed|Cancelled|Refunded|Expired`). Cần chốt 1 schema thống nhất.
- **Bắt buộc**: ✅ (trước khi code BE-060 Booking).
- **Hướng dẫn**: Trả lời các câu hỏi trong `audit-old-backend.md §5` và §6.
- **Verify**: `decisions.md` có DEC-026 chốt enum + mapping.

## UA-019 — Chốt payment method enum

- **Lý do**: Web admin mock có 4 method (FitMate Pay, QR, MoMo, Napas). BE OpenAPI chỉ có `Vnpay`. Cần biết MVP support 1 hay multi-provider.
- **Bắt buộc**: ✅ (trước khi code BE-140 Payment).
- **Hướng dẫn**: Trả lời trong `audit-old-backend.md §6`.
- **Verify**: `decisions.md` có DEC-027.

## UA-020 — Chốt Report target type

- **Lý do**: Mobile có 4 (`post|comment|user|certificate`), web có 3 (`post|user|pt`), BE OpenAPI chưa rõ. Cần biết phạm vi.
- **Bắt buộc**: ✅ (trước khi code BE-200 Moderation).
- **Hướng dẫn**: Trả lời trong `audit-old-backend.md §6`.
- **Verify**: `decisions.md` có DEC-028.

## UA-021 — Chốt cấu hình managed Postgres

- **Lý do**: Có 4 ứng viên (DO Managed / AWS RDS / Neon / Supabase). Mỗi cái có cost / HA / connection limit khác.
- **Bắt buộc**: ✅ (trước khi production DB).
- **Hướng dẫn**: Trả lời 4 câu hỏi trong `managed-postgres-comparison.md §5`.
- **Verify**: `decisions.md` DEC-022 chốt provider + tier.

## UA-022 — Chốt platform fee API có cho phép admin sửa không

- **Lý do**: Web admin có `platformPolicy.platformFeeRate=10` (cố định). BE OpenAPI chưa có endpoint quản lý.
- **Bắt buộc**: ⚠️ (chốt trước MVP).
- **Hướng dẫn**: Trả lời trong `audit-old-backend.md §6`.
- **Verify**: `decisions.md` có DEC-029.

## UA-023 — Cấp managed Postgres credentials

- **Lý do**: Sau khi chốt provider (UA-021), cần connection string thật.
- **Bắt buộc**: ✅ (trước khi deploy production DB).
- **Hướng dẫn**: Tạo cluster, lấy connection string có TLS, set `sslmode=require`.
- **Verify**: `psql` connect OK từ VPS production.

## UA-024 — Xác minh Google Play policy mới nhất

- **Lý do**: Policy Google Play thay đổi thường xuyên. File `vnpay-googleplay-policy.md` tra cứu 2026-09-18.
- **Bắt buộc**: ✅ (trước khi submit lên Play).
- **Hướng dẫn**: Vào https://support.google.com/googleplay/android-developer, đọc mục Payments / Health / UGC / Account Deletion / Data Safety. So sánh với `vnpay-googleplay-policy.md §2`. Update nếu khác.
- **Verify**: Ghi ngày tra cứu mới nhất vào file.

## Secrets không bao giờ commit

- File `.env` thật → dùng secret manager.
- Keystore / upload key JSON → secret manager.
- DB password, JWT signing key, SMTP, payment secret → secret manager.
