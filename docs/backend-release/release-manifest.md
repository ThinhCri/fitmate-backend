# Release Manifest — Fitmate Production Baseline

**Mã tài liệu:** REL-MANIFEST-2026-09-24-v1  
**Ngày lập:** 2026-09-24  
**Môi trường đích:** Staging / Production  
**Quyết định phát hành hiện tại:** **NO-GO** (Gate G7 UAT ba repo, G3 Live VNPay và G8 Offsite Keys đang PENDING / BLOCKED)

---

## 1. Định danh Artifact và Source Revisions

| Thành phần | Repository / Nguồn | Revision / Commit SHA | Trạng thái Artifact / Checksum | Ghi chú |
|---|---|---|---|---|
| **Backend API** | `FitmateAI_BE` | `d0bdb47b31c7fe9e1dd44834f76d9f5e397990ba` | Image: `fitmate-api:1.0.0-staging`<br>Digest: `PENDING_BUILD` | Branch: `backend-release/contract-and-foundation` |
| **Android App** | `FitmateAI_FE` (thư mục `mobile/`) | `bb8207f68788677f57463fe00ce50860d6a3851c` | Package: `vn.fitmate.app`<br>versionCode: `1`<br>versionName: `0.1.0`<br>AAB Checksum: `PENDING_GRADLE_BUNDLE` | React Native (Expo SDK 57), Target SDK 34+ |
| **Admin WebApp**| `FitmateWebApp` | `76766f8e2f37474ab3467307ba0cf9af71c74d6a` | Dist Bundle: `dist/`<br>SHA-256: `PENDING_VITE_BUILD` | Vite + React SPA |
| **OpenAPI Spec** | `docs/backend-release/openapi/fitmate-v1.yaml` | `d0bdb47b31c7fe9e1dd44834f76d9f5e397990ba` | SHA-256: `3B2E624616A3999DDDC4288E430866C8CD236F0E70430B22541B1556AF4496F6` | Đã đóng băng contract, cấm breaking wire change |
| **DB Migrations**| `Fitmate.Infrastructure/Migrations` | `d0bdb47b31c7fe9e1dd44834f76d9f5e397990ba` | From: `0` (Clean DB)<br>To: `20260923180000_BaselineV1` | Tool: `dotnet-ef 10.0.0`, idempotent script bundle |

> [!NOTE]
> Các trường `PENDING_BUILD`, `PENDING_GRADLE_BUNDLE`, `PENDING_VITE_BUILD` sẽ được thay thế bằng mã băm SHA-256 cụ thể ngay sau khi lệnh build release chính thức trên CI server hoàn tất.

---

## 2. Thông số Runtime và Hạ tầng

| Hạng mục | Quy cách kỹ thuật | Ghi chú tương thích |
|---|---|---|
| **.NET SDK & Runtime** | .NET 10.0 LTS (SDK `10.0.100`, rollForward `latestFeature`) | Hỗ trợ chính thức đến 14/11/2028 theo Microsoft Support Lifecycle |
| **ORM / Data Access** | Entity Framework Core `10.0.0` + Npgsql `10.0.0` | Đã nghiệm thu 586 unit tests và 19 integration tests tương thích |
| **Cơ sở dữ liệu** | PostgreSQL 16 LTS (Alpine container hoặc Managed Cloud PostgreSQL) | Bộ ký tự UTF-8, timezone UTC |
| **Base Docker Image** | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` | Chiseled Ubuntu Noble (non-root, minimal attack surface, no shell) |
| **Mạng & TLS** | HTTPS / TLS 1.2+ (Reverse Proxy / Ingress Managed TLS) | Chặn toàn bộ plaintext HTTP |

---

## 3. Trạng thái Đóng băng Tính năng (Capabilities State)

Theo ranh giới thiết kế tại **BE-829**, các tính năng sau bắt buộc giữ trạng thái đóng băng trong đợt phát hành baseline core:

```json
{
  "ai": false,
  "vipPurchase": false,
  "rewardCashout": false,
  "ptAutomaticPayout": false,
  "accountDeletionUrl": "https://fitmate.vn/account-deletion"
}
```

- **Ai = false**: Các tính năng AI meal scan / automated workout generation trả về HTTP 503 / Feature Disabled rõ ràng.
- **VipPurchase = false**: Không kích hoạt bán gói VIP/Subscription số trên ứng dụng.
- **RewardCashout = false**: Không kích hoạt rút tiền thưởng hay quy đổi điểm số sang tiền tệ.
- **PtAutomaticPayout = false**: Doanh thu PT được tính toán theo quy tắc 10% platform / 90% PT; lệnh quyết toán (payout) thực hiện đối soát và chuyển khoản ngân hàng thủ công qua Admin portal, không tự động trigger API ngân hàng.

---

## 4. Chế độ Tích hợp Nhà cung cấp Dịch vụ (Provider Modes)

| Provider | Chế độ phát hành hiện tại | Yêu cầu trước khi Go-Live Production | Trạng thái Gate |
|---|---|---|---|
| **VNPay Gateway** | **Sandbox Mode** (test credentials giả lập) | Cần bộ Merchant Credentials thật từ hợp đồng doanh nghiệp (Merchant ID, Hash Secret, Production URL) theo OPS-03 | **BLOCKED** |
| **Email Service** | **Outbox / Local MailHog** | Cần tài khoản SMTP Transactional (SendGrid, SES hoặc Mailgun) đã verify domain SPF/DKIM theo OPS-02 | **BLOCKED** |
| **Storage (Object/S3)** | **Local HMAC Storage** | Cần Cloudflare R2 / AWS S3 Production Bucket kèm IAM role phân quyền tối thiểu theo OPS-04 | **BLOCKED** |
| **Push Notifications** | **In-app / FCM Stub** | Cần tệp Service Account JSON của Firebase Cloud Messaging theo OPS-05 | **BLOCKED** |

---

## 5. Danh mục Tham chiếu Cấu hình Bí mật (Secret Reference Names ONLY)

> [!WARNING]
> Tuyệt đối không ghi giá trị bí mật vào bất kỳ tệp manifest hay kho lưu trữ mã nguồn nào. Dưới đây chỉ là danh mục định danh khóa cấu hình mà Operator cần cung cấp trên hệ thống quản lý bí mật (Secret Store / Environment Variables):

1. `ConnectionStrings__DefaultConnection` (Chuỗi kết nối PostgreSQL production)
2. `Jwt__SecretKey` (Khóa ký JWT tối thiểu 256-bit)
3. `VNPay__TmnCode` (Mã định danh Merchant VNPay)
4. `VNPay__HashSecret` (Khóa bí mật tạo checksum HMAC-SHA512 VNPay)
5. `VNPay__PaymentUrl` (URL cổng thanh toán VNPay)
6. `Storage__S3__AccessKeyId` (Access Key lưu trữ S3/R2)
7. `Storage__S3__SecretAccessKey` (Secret Key lưu trữ S3/R2)
8. `Storage__S3__BucketName` (Tên bucket lưu trữ)
9. `Email__Smtp__Host`, `Email__Smtp__Port`, `Email__Smtp__Username`, `Email__Smtp__Password` (Thông số máy chủ SMTP)
10. `Firebase__ServiceAccountKeyJson` (Chuỗi JSON thông tin xác thực Firebase Admin SDK)

---

## 6. Liên kết Bằng chứng Nghiệm thu (Evidence Links)

- [BE-800 Baseline Survey](production-plans/evidence/BE-800.md) (VERIFIED: Build 0 error, 402/402 tests PASS)
- [BE-801 Contract Freeze](production-plans/evidence/BE-801.md) (VERIFIED: Route inventory frozen)
- [BE-802 Auth & Recovery](production-plans/evidence/BE-802.md) (VERIFIED: 410/410 tests PASS)
- [BE-803 Admin MFA](production-plans/evidence/BE-803.md) (VERIFIED: 411/411 tests PASS)
- [BE-804 User Profile & Privacy](production-plans/evidence/BE-804.md) (VERIFIED: 417/417 tests PASS)
- [BE-805 Account Deletion & Retention](production-plans/evidence/BE-805.md) (VERIFIED: 423/423 tests PASS)
- [BE-806 PT Onboarding & Reviews](production-plans/evidence/BE-806.md) (VERIFIED: 429/429 tests PASS)
- [BE-807 Booking State Machine](production-plans/evidence/BE-807.md) (VERIFIED: 442/442 tests PASS)
- [BE-808 Payment Core & Idempotency](production-plans/evidence/BE-808.md) (VERIFIED: 457/457 tests PASS)
- [BE-809 Late IPN & Concurrency](production-plans/evidence/BE-809.md) (VERIFIED: 472/472 tests PASS)
- [BE-810 PT Revenue & Manual Payout](production-plans/evidence/BE-810.md) (VERIFIED: 485/485 tests PASS)
- [BE-811 Workouts & Weight Logs](production-plans/evidence/BE-811.md) (VERIFIED: 495/495 tests PASS)
- [BE-812 Nutrition & Meal Plans](production-plans/evidence/BE-812.md) (VERIFIED: 507/507 tests PASS)
- [BE-813 Invariant Dashboard](production-plans/evidence/BE-813.md) (VERIFIED: 519/519 tests PASS)
- [BE-814 Social Safety & Moderation](production-plans/evidence/BE-814.md) (VERIFIED: 531/531 tests PASS)
- [BE-815 In-App Notifications & FCM](production-plans/evidence/BE-815.md) (VERIFIED: 541/541 tests PASS)
- [BE-816 Storage & Private Media HMAC](production-plans/evidence/BE-816.md) (VERIFIED: 550/550 tests PASS)
- [BE-817 AI Unavailable Degradation](production-plans/evidence/BE-817.md) (VERIFIED: 556/556 tests PASS)
- [BE-818 Admin Moderation & Audit Logs](production-plans/evidence/BE-818.md) (VERIFIED: 568/568 tests PASS)
- [BE-819 Support Tickets & SLA](production-plans/evidence/BE-819.md) (VERIFIED: 576/576 tests PASS)
- [BE-820 Contract Breaking Diff Guard](production-plans/evidence/BE-820.md) (VERIFIED: 581/581 tests PASS)
- [BE-821 CI Pipeline & Docker Builds](production-plans/evidence/BE-821.md) (VERIFIED: Docker builds PASS)
- [BE-822 Production Secrets Allowlist](production-plans/evidence/BE-822.md) (VERIFIED: Sanitized secrets)
- [BE-823 Load & Performance Baseline](production-plans/evidence/BE-823.md) (VERIFIED: k6 scenarios ready)
- [BE-824 DB Failover & Pool Recovery](production-plans/evidence/BE-824.md) (VERIFIED: Transient retry verified)
- [BE-825 Disaster Recovery & Ledger Drill](production-plans/evidence/BE-825.md) (VERIFIED: Recovery scripts & invariants)
- [BE-826 UAT Ba Repo & Staging](production-plans/evidence/BE-826.md) (Backend API 10/10 PASS; Mobile & Web Staging PENDING)
- [BE-828 .NET 10 LTS Runtime](production-plans/evidence/BE-828.md) (VERIFIED: .NET 10 full test suite PASS)
- [BE-829 VIP & Rewards Backlog Isolation](production-plans/evidence/BE-829.md) (VERIFIED: 586/586 tests PASS)
- [BE-827 Chốt Production và Play Release](production-plans/evidence/BE-827.md) (Tài liệu này — Trạng thái: **NO-GO**)

