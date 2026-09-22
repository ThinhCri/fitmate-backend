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

- **Ngày chốt**: 2026-09-18
- **Lý do**: 3 client có 3 schema lệch nhau; nhiều record không đủ dữ kiện suy ra mapping an toàn.
- **Quyết định**: `decisions.md` **DEC-026** — `AppointmentStatus` 7 value (PascalCase: Held, Pending, Confirmed, InProgress, Completed, Cancelled, Expired). `PaymentStatus` + `RefundStatus` tách riêng. `late` là flag `is_late_attendee`; `rescheduled` là event `BookingRescheduleEvent`. Mapping chi tiết: `api-contract.md §2.1`.
- **Trạng thái**: ✅ DONE (phiên 7, 2026-09-18).

## UA-019 — Chốt payment method enum

- **Ngày chốt**: 2026-09-18
- **Quyết định**: `decisions.md` **DEC-027** — release đầu chỉ triển khai VNPay. Enum API `Payment.method = vnpay` (1 value). IPaymentProvider interface giữ để mở rộng sau, không viết nhiều adapter giả (DEC-033). FE UI rút gọn chip về 1 (TODO out-of-scope `docs/integration/web-payment-chip.md`).
- **Trạng thái**: ✅ DONE (phiên 7).

## UA-020 — Chốt Report target type

- **Ngày chốt**: 2026-09-18
- **Quyết định**: `decisions.md` **DEC-028** — enum canonical 4 value (PascalCase: Post, Comment, User, Certificate). PT map sang user. Visibility check + reason validate + idempotency chống spam. Block user là action riêng, không tự động từ report.
- **Trạng thái**: ✅ DONE (phiên 7).

## UA-021 — Chốt cấu hình managed Postgres

- **Ngày chốt**: 2026-09-18
- **Quyết định**: `decisions.md` **DEC-030** (DEC-022 cập nhật thành Accepted). Production = DigitalOcean Managed PostgreSQL Singapore, primary 2 vCPU / 4 GiB + 1 standby HA; **chưa có read replica** (phân biệt với standby). Storage ~30 GiB. Region Singapore, BE đặt cùng region + private networking khi khả dụng.
- **Blocked area**: SKU / pricing / backup retention / connection limit phải xác minh trên nguồn chính thức trước khi cấp phát. Không tự tạo tài nguyên trả phí.
- **Trạng thái**: ✅ Baseline DONE (phiên 7). UA-023 vẫn pending credentials khi deploy production.

## UA-022 — Chốt platform fee API có cho phép admin sửa không

- **Ngày chốt**: 2026-09-18
- **Quyết định**: `decisions.md` **DEC-029** — release đầu **read-only**. Phí nền tảng cố định **10%** = `platform_fee_rate_bps = 1000`, đọc từ server config (`PLATFORM_FEE_BPS` env). Endpoint mới: `GET /admin/platform-policy`. KHÔNG nhận fee rate từ client.
- **Trạng thái**: ✅ DONE (phiên 7). POST sửa phí là post-MVP.

## UA-023 — Cấp managed Postgres credentials

- **Trạng thái**: ⏳ Pending — credentials qua secret manager, **không gửi connection string thật qua chat**.
- **Phụ thuộc**: UA-021 (đã chốt baseline ở DEC-030).
- **Hướng dẫn**: Tạo cluster ở DigitalOcean Singapore theo `managed-postgres-comparison.md` §3 (configuration baseline DEC-030). Connection string có TLS, set `sslmode=require`. Lưu secret qua Vault / SOPS / GitHub Actions secret — không commit.
- **Verify**: `psql "sslmode=require ..."` từ VPS production connect OK. BE startup không panic ở DB connection.
- **Note**: Không chặn việc code backend / migration / integration test local — dev dùng Postgres Docker Compose (xem `AGENTS.md §Vai trò của repo`).

## UA-024 — Xác minh Google Play policy mới nhất

- **Ngày chốt phần rà soát tài liệu**: 2026-09-18 (phiên 7). File `vnpay-googleplay-policy.md` đã viết lại với mapping chính xác theo nguồn chính thức.
- **Trạng thái**: ✅ Rà soát tài liệu DONE. Submit + verify trên Console = action riêng (UA-010..014).
- **User action còn lại** (KHÔNG chặn backend release):
  - Verify lại lần cuối trước khi submit app lên internal-testing / production.
  - Khai `Data Safety form` trên Play Console (UA-013).
  - Đăng ký Privacy policy + Terms URL (UA-012).
- **Note quan trọng** (DEC-031): Không viết "Google Play compliant" chỉ từ việc đọc policy. Việc trên Console là khác biệt với rà soát tài liệu.

## UA-FCM-001 — Chốt push notification provider (Expo Push Token vs FCM trực tiếp)

- **Ngày tạo**: 2026-09-20 (BE-200 khảo sát)
- **Lý do**: BE-200 Notifications cần wire `POST /users/me/devices` với provider cụ thể để validate format token + adapter FCM/Expo. FE chưa có implementation push trong repo hiện tại (BE-200 khảo sát 2026-09-20: thấy `expo-router`, `expo-secure-store`, `expo-image-picker`, **không** thấy `expo-notifications` hay `@react-native-firebase/messaging`).
- **Bắt buộc**: ✅ (trước khi triển khai BE-200c Push adapter)
- **Hướng dẫn**:
  - Repo `FitmateAI_FE` đang dùng **Expo SDK** (managed workflow). Recommended: **Expo Push Token** (`ExponentPushToken[...]`) — đơn giản, miễn phí tier 1, không cần GoogleService-Info.plist.
  - Nếu chọn **FCM trực tiếp** → cần service account JSON qua UA-009-style secret manager + `google-services.json` cho Android.
  - Backend BE-200 chỉ validate format `device_token` rất lỏng (`Length 32..4096`, charset `[A-Za-z0-9_\-:.=+/]`) trong MVP; validation cứng theo provider là task BE-200c.
- **Verify**: User confirm Expo Push Token hay FCM, cập nhật DEC-044 với quyết định.
- **Task bị chặn**: BE-200c (FCM/Expo adapter). BE-200 in-app notification KHÔNG bị chặn bởi UA này.
- **Trạng thái**: ⏳ Pending (BE-200 DONE sau khi DEC-044 chốt).

## Secrets không bao giờ commit

- File `.env` thật → dùng secret manager.
- Keystore / upload key JSON → secret manager.
- DB password, JWT signing key, SMTP, payment secret → secret manager.
