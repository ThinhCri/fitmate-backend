# Decisions Log

> Mỗi quyết định kiến trúc/sản phẩm đáng kể ghi ở đây. Format: DEC-NNN, ngày, người chốt, bối cảnh, quyết định, hệ quả, trạng thái.

---

## DEC-001 — Stack backend: .NET 8 + PostgreSQL self-hosted

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: BE Spring Boot + Supabase cũ chỉ có scaffold Auth, security mở toang, secrets đã commit. Cần stack phù hợp self-host, có ecosystem tốt cho nghiệp vụ tài chính.
- **Quyết định**: Viết lại BE bằng **.NET 8 LTS + ASP.NET Core Web API + EF Core 8 + Npgsql + PostgreSQL 16 self-hosted (Docker Compose)**. Auth dùng **JWT custom** do BE cấp, không phụ thuộc Supabase.
- **Hệ quả**:
  - Cần tạo lại toàn bộ entities, controllers, services. Scaffold cũ giữ làm tham chiếu nội dung.
  - Migration strategy: Supabase → self-hosted Postgres, expand → migrate → contract.
  - Supabase credentials đã commit phải được vô hiệu hóa và xóa khỏi lịch sử (chờ user xác nhận rotate).
  - Payment provider: **VNPay sandbox** (chốt ở DEC-002).
  - Deploy: **Docker Compose** trên 1 VPS (chốt ở DEC-003).
- **Trạng thái**: ✅ Accepted, bắt đầu triển khai docs nền tảng.

## DEC-002 — Payment provider: VNPay sandbox

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: Cần provider cho booking payment; chưa có merchant thật.
- **Quyết định**: Tích hợp **VNPay sandbox** cho giai đoạn test. Không tạo merchant account thật.
- **Hệ quả**:
  - Phải khảo sát tài liệu VNPay hiện hành: TMN, hash secret, return URL, IPN URL.
  - Fake payment chỉ local/dev; production từ chối cấu hình fake, không trả success giả.
  - Webhook phải verify HMAC + idempotency.
- **Trạng thái**: ✅ Accepted.

## DEC-003 — Deploy target: Docker Compose trên 1 VPS

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: MVP nhiều người dùng nhưng chưa cần scale ngang.
- **Quyết định**: **Docker Compose** trên 1 VPS cho staging/production đầu tiên. KHÔNG K8s.
- **Hệ quả**:
  - Stateless app + Postgres volume + Redis (chỉ thêm khi đo cần).
  - Migrations chạy qua job riêng, không phải container chính.
  - Capacity plan sẽ giả định 1 replica; nếu cần scale sẽ revisit.
- **Trạng thái**: ✅ Accepted.

## DEC-004 — Không microservices, không K8s, không Redis ở MVP

- **Ngày**: 2026-09-18
- **Quyết định**: Modular monolith. Chỉ thêm Redis/queue/service riêng khi có bằng chứng bottleneck từ load test.
- **Hệ quả**: Capacity plan sẽ chứng minh giới hạn của cấu hình hiện tại.
- **Trạng thái**: ✅ Accepted.

## DEC-005 — Contract-first cho cả 2 frontend

- **Ngày**: 2026-09-18
- **Quyết định**: Viết OpenAPI 3.1 spec trước (openapi/fitmate-v1.yaml). FE Android + Admin Web đọc contract; không tự tích hợp trong gói này.
- **Hệ quả**:
  - OpenAPI là nguồn chuẩn. Implementation phải match.
  - FE có thể generate client (openapi-typescript / openapi-generator).
  - Breaking change yêu cầu version mới (`v2`).
- **Trạng thái**: ✅ Accepted.

## DEC-006 — Tiền: integer VND

- **Ngày**: 2026-09-18
- **Quyết định**: Mọi cột tiền `BIGINT`. Không `DECIMAL`, không `FLOAT`.
- **Hệ quả**:
  - Phí nền tảng tính integer division + rounding half-up.
  - Refund cộng dồn tránh lệch làm tròn.
- **Trạng thái**: ✅ Accepted.

## DEC-007 — Timestamp UTC + timezone riêng

- **Ngày**: 2026-09-18
- **Quyết định**: DB lưu UTC. API trả ISO-8601 UTC + field timezone. Lọc ngày theo local timezone của PT cho lịch.
- **Hệ quả**: Cần lưu `slot_start_local` + `slot_start_utc` + `trainer_timezone`.
- **Trạng thái**: ✅ Accepted.

## DEC-008 — Phí nền tảng 10% trên actual_paid_vnd

- **Ngày**: 2026-09-18
- **Quyết định**: Phí = 10% × actual_paid_vnd, rounding half-up. PT share = actual_paid_vnd - fee.
- **Hệ quả**:
  - Refund giảm cả phần fee và PT share tỷ lệ.
  - Booking chưa completed không ghi nhận doanh thu cho admin dashboard.
- **Trạng thái**: ✅ Accepted.

## DEC-009 — Booking không auto-complete khi quá giờ

- **Ngày**: 2026-09-18
- **Quyết định**: Trạng thái `completed` chỉ do PT (hoặc admin moderation) set thủ công.
- **Hệ quả**: Tránh tranh chấp; có thể thêm reminder; release slot quá giờ cần policy riêng (chưa chốt).
- **Trạng thái**: ✅ Accepted.

## DEC-010 — Hủy trước completed: hoàn toàn bộ

- **Ngày**: 2026-09-18
- **Quyết định**: Hủy sau `paid`, trước `completed` → refund toàn bộ `actual_paid_vnd`. Chưa có phí phạt hủy.
- **Hệ quả**: Revenue dashboard chỉ tính `paid + completed`; refund là điều chỉnh kỳ.
- **Trạng thái**: ✅ Accepted (chính sách phát triển).

## DEC-011 — Idempotency cho mọi mutation có side effect tài chính

- **Ngày**: 2026-09-18
- **Quyết định**: Endpoint tài chính yêu cầu header `Idempotency-Key`. Lưu key + payload hash + response snapshot.
- **Hệ quả**: Retry an toàn; chống tính trùng.
- **Trạng thái**: ✅ Accepted.

## DEC-012 — Outbox pattern cho side effect bất đồng bộ

- **Ngày**: 2026-09-18
- **Quyết định**: Email / push / reconciliation job đi qua Outbox table + worker.
- **Hệ quả**: Không lệch giữa DB change và side effect.
- **Trạng thái**: ✅ Accepted.

## DEC-013 — Không seed admin mặc định

- **Ngày**: 2026-09-18
- **Quyết định**: Admin đầu tiên tạo qua CLI, không có password default.
- **Hệ quả**: User/operator phải chủ động bootstrap.
- **Trạng thái**: ✅ Accepted.

## DEC-014 — Không commit secrets; .env.example là placeholder

- **Ngày**: 2026-09-18
- **Quyết định**: Repo chỉ có `.env.example`. `.env` thật ở local/CI/prod qua secret manager. CI scan secrets.
- **Hệ quả**: Ngăn chặn lộ Supabase/DB/JWT/keystore credentials như đã xảy ra ở BE cũ.
- **Trạng thái**: ✅ Accepted.

## DEC-015 — Frontend đọc OpenAPI, không tự integrate trong gói này

- **Ngày**: 2026-09-18
- **Quyết định**: Trong gói backend release, FE Android và Admin Web chỉ nhận contract + integration guide. Tự integrate là task riêng.
- **Hệ quả**: Một số fix build/CI trên FE có thể cần nhưng không đổi nghiệp vụ.
- **Trạng thái**: ✅ Accepted.

## DEC-016 — Chưa tích hợp AI production

- **Ngày**: 2026-09-18
- **Quyết định**: AI contract đề xuất (xem `ai-contract.md`). Adapter trả `AI_UNAVAILABLE` khi provider lỗi hoặc chưa bàn giao.
- **Hệ quả**: Chức năng không cần AI chạy bình thường.
- **Trạng thái**: ✅ Accepted.

## DEC-017 — Không tự deploy production trong gói này

- **Ngày**: 2026-09-18
- **Quyết định**: Chuẩn bị staging pipeline + runbook; chưa tự chạy production deploy.
- **Hệ quả**: Cần user cấp quyền deploy + secrets khi sẵn sàng.
- **Trạng thái**: ✅ Accepted.

## DEC-018 — Capacity plan dùng giả định có document

- **Ngày**: 2026-09-18
- **Quyết định**: Chưa có số liệu kinh doanh thật → đề xuất mô hình tải tạm, dán nhãn "giả định".
- **Hệ quả**: Load test phải nêu rõ giả định; không cam kết production capacity.
- **Trạng thái**: ✅ Accepted.

## DEC-019 — Logging structured (Serilog JSON), không log secrets

- **Ngày**: 2026-09-18
- **Quyết định**: Log Serilog JSON; có `traceId`, `userId`, `path`, `latencyMs`. Không log password, hash, JWT, secret key.
- **Hệ quả**: Audit + debug hiệu quả; an toàn.
- **Trạng thái**: ✅ Accepted.

## DEC-020 — Không giữ transaction mở khi gọi provider

- **Ngày**: 2026-09-18
- **Quyết định**: Payment / external HTTP call không nằm trong DB transaction. Commit DB trước, dispatch qua Outbox.
- **Hệ quả**: Tránh lock DB lâu; rollback rõ ràng.
- **Trạng thái**: ✅ Accepted.

## DEC-021 — Auth: ASP.NET Core Identity

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: Cần password hashing đúng chuẩn, lockout, email confirm, password reset có sẵn.
- **Quyết định**: Dùng `Microsoft.AspNetCore.Identity.EntityFrameworkCore` với `ApplicationUser : IdentityUser<Guid>` + `IdentityRole<Guid>`. JWT issuance + refresh token vẫn tự viết (Identity không có).
- **Hệ quả**: Schema Identity có 8 bảng (Users, Roles, UserRoles, UserClaims, RoleClaims, UserLogins, UserTokens). Mất ~150 LOC custom hashing. Được maintain bởi Microsoft.
- **Chi tiết**: [`identity-evaluation.md`](identity-evaluation.md).
- **Trạng thái**: ✅ Accepted.

## DEC-022 — Managed PostgreSQL cho production (baseline kỹ thuật)

- **Ngày**: 2026-09-18
- **Người chốt**: User (xác nhận qua UA-021 → chi tiết ở DEC-030)
- **Bối cảnh**: DEC-030 đã chốt provider + tier + region. DEC-022 giữ baseline ngắn gọn, chi tiết kỹ thuật ở DEC-030.
- **Quyết định**: Production target = DigitalOcean Managed PostgreSQL, Singapore region, primary 2 vCPU / 4 GiB RAM + 1 standby HA (chưa có read replica). Local = Docker Compose. Staging = môi trường tách biệt (chốt sau khi có budget).
- **Hệ quả**: xem DEC-030.
- **Trạng thái**: ✅ Accepted (baseline).

## DEC-023 — Google Play: Booking PT là dịch vụ vật lý → không bắt buộc Play Billing

- **Ngày**: 2026-09-18
- **Người chốt**: User (qua xác nhận chính sách VNPay)
- **Bối cảnh**: Cần xác định loại sản phẩm Fitmate theo Google Play policy.
- **Quyết định**:
  - Booking PT = dịch vụ vật lý → VNPay OK, không vi phạm Play Billing policy.
  - Nếu sau này thêm subscription/premium digital content → PHẢI qua Play Billing.
  - Health declaration: KHÔNG claim chẩn đoán/điều trị → khai là "lifestyle/fitness".
  - Account deletion: bắt buộc có flow `DELETE /users/me`.
  - UGC: đã có report + admin moderation flow.
- **Hệ quả**: 
  - Phí provider VNPay tách khỏi phí nền tảng 10%.
  - Cần privacy policy URL.
- **Chi tiết**: [`vnpay-googleplay-policy.md`](vnpay-googleplay-policy.md).
- **Trạng thái**: ✅ Accepted.

## DEC-024 — Migration từ BE cũ: viết mới, KHÔNG migrate data

- **Ngày**: 2026-09-18
- **Người chốt**: User (xác nhận qua audit)
- **Bối cảnh**: BE cũ chỉ có 3 endpoint (register, login, getUserProfile) + 12 entity sơ khai. JWT giả. Không có data thật.
- **Quyết định**:
  - Giữ BE cũ trong Git (tham chiếu nội dung), KHÔNG xóa.
  - Viết mới .NET 8 BE từ đầu. Parity ~3/69 endpoints, 97% là mới.
  - Demo data sẽ tạo qua seed script, không migrate từ Supabase.
  - Nếu sau này user có data thật trong Supabase → viết export script riêng (chờ UA-001).
- **Hệ quả**: Schema cũ không mapping sang schema mới (DateTime, JSON-as-text, etc đều khác). Tốn công đọc spec hơn migration data.
- **Chi tiết**: [`audit-old-backend.md`](audit-old-backend.md).
- **Trạng thái**: ✅ Accepted.

## DEC-025 — Staging có thể dùng Docker Compose trên 1 VPS

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: Staging chưa cần scale.
- **Quyết định**: 1 droplet 4 GB Singapore chạy compose (`api`, `postgres`, `mailhog`). Backup nightly qua DO Spaces.
- **Hệ quả**: Downtime khi droplet fail — acceptable cho staging.
- **Trạng thái**: ✅ Accepted.

## DEC-026 — AppointmentStatus: 7-state, tách khỏi Payment/Refund

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: Mobile có 6 trạng thái lịch, web admin có 4, BE OpenAPI cũ gộp trạng thái lịch + thanh toán + hoàn tiền thành `BookingStatus` 7 value. Dữ liệu cũ mơ hồ — nhiều record không đủ dữ kiện để suy ra mapping an toàn. Xem `audit-old-backend.md §5,§6`.
- **Quyết định**:
  - **Không gộp** trạng thái lịch hẹn / thanh toán / hoàn tiền vào một enum.
  - **AppointmentStatus** (mới, thay cho `BookingStatus`) — **7 value** ổn định, dùng đúng casing PascalCase trong OpenAPI:
    - `Held`: giữ chỗ tạm thời, có TTL.
    - `Pending`: đang chờ PT xác nhận.
    - `Confirmed`: PT đã xác nhận.
    - `InProgress`: buổi tập đã bắt đầu.
    - `Completed`: buổi tập đã hoàn thành.
    - `Cancelled`: đã hủy.
    - `Expired`: giữ chỗ/yêu cầu đã hết hạn.
  - **PaymentStatus** (enum riêng): `Pending | Paid | Failed | Refunded | PartialRefunded`.
  - **RefundStatus** (enum riêng): `Pending | Succeeded | Failed`.
  - **`Paid` / `Refunded` không phải AppointmentStatus** — đó là trạng thái của payment/refund. Read API có thể trả cả ba trên cùng response.
  - **`Confirmed` chỉ nghĩa là lịch được xác nhận** — không đồng nghĩa đã thanh toán. Bắt đầu buổi tập / ghi nhận doanh thu phải kiểm tra cả `AppointmentStatus` + `PaymentStatus`.
  - **Late là thông tin attendance/arrival**, không phải trạng thái lịch hẹn. Không tự kết luận no-show chỉ vì quá giờ.
  - **Rescheduled là sự kiện có audit**, không phải trạng thái kết thúc. Giữ lịch sử giờ cũ + giờ mới + người yêu cầu + người chấp nhận qua `previous_booking_id` + `BookingRescheduleEvent`. Không mất slot cũ nếu đổi lịch thất bại (transaction có check slot mới).
- **Mapping 2 chiều (FE adapter)**:
  | Mobile (6) | Web (4) | AppointmentStatus (BE mới) |
  |---|---|---|
  | `pending` | `pending` | `Held` hoặc `Pending` |
  | `confirmed` | — | `Confirmed` |
  | `completed` | `completed` | `Completed` |
  | `cancelled` | `cancelled` | `Cancelled` |
  | — | `in_progress` | `InProgress` |
  | — | — | `Expired` |
  | (late = flag) | — | (giữ status, flag `is_late_attendee`) |
  | (rescheduled = event) | — | (Cancelled → new booking) |
- **Hệ quả**:
  - OpenAPI: schema `BookingStatus` → `AppointmentStatus`, enum đổi sang PascalCase 7 value trên.
  - Appointment schema thêm field `payment_status` + `refund_status` + `is_late_attendee` + `previous_booking_id?`.
  - Bảng `booking_reschedule_events` mới để audit reschedule (audit trail: old slot, new slot, requested_by, accepted_by, reason).
  - **Dữ liệu cũ mơ hồ**: BE mới là greenfield — không có data migration (DEC-024). Mock FE vẫn hoạt động qua adapter. Bất kỳ record FE nào gặp data thiếu dữ kiện — render "—" chứ không suy luận.
  - Viết `mobile/src/adapters/booking-status.ts` để map 2 chiều mobile ↔ BE.
- **Trạng thái**: ✅ Accepted.

## DEC-027 — Payment MVP: chỉ VNPay

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: Web admin mock có 4 method (FitMate Pay / QR / MoMo / Napas), BE OpenAPI cũ enum `Vnpay`. Các provider ngoài chưa có merchant credentials, việc giữ các lựa chọn ảo trong API chỉ vì frontend mock có là sai lý do.
- **Quyết định**:
  - **Release đầu tiên chỉ triển khai VNPay**. Chưa triển khai MoMo, Stripe, ví FitMate Pay, provider khác.
  - **`Payment.method` enum trong OpenAPI: 1 value `Vnpay`** (không mở rộng enum rộng cho tương lai).
  - **Giữ `IPaymentProvider` interface** trong Application layer (`CreatePaymentAsync`, `VerifySignature`, `QueryTransactionAsync`, `RefundAsync`, `ParseProviderReturn`) để mở rộng sau khi có merchant thật. **Không viết nhiều adapter giả** không dùng đến — YAGNI.
  - **Phân biệt `provider` ↔ `method/channel`**: provider = `Vnpay` (enum). `method/channel` (QR / card / bank) là thông tin phụ nếu VNPay sandbox/production trả về và model nào đó thực sự cần hiển thị — không phải enum API.
  - **QR / NAPAS không phải provider ngang VNPay**. Nếu sau này cần thêm phương thức → đăng ký provider thật, viết adapter, đẩy OpenAPI enum.
  - **Fake payment chỉ ở local/test**. Production fail-fast nếu cấu hình sandbox — adapter throw `InvalidOperationException` ở startup nếu `ASPNETCORE_ENVIRONMENT=Production` mà config payment là sandbox.
- **Hệ quả**:
  - OpenAPI `Payment.method` enum = `Vnpay`. Schema enum rút gọn từ 5 value (proposed) về 1.
  - FE cần đổi payment UI: các chip "Fitmate Pay / QR / Momo / Napas" thay bằng 1 nút "Thanh toán qua VNPay". Đây là việc trong integration guide (FE), không tự sửa FE ở giai đoạn backend.
  - Integration guide bổ sung: FE thay các lựa chọn mock bằng provider đang được BE hỗ trợ.
  - Test `POST /payments` với `method != vnpay` → 400 `FITMATE.PAYMENT.PROVIDER_NOT_SUPPORTED`.
  - Adapter VNPay fail-fast nếu secret rỗng ở Production.
  - Sandbox E2E + IPN verify + idempotency + reconcile phải pass (xem `vnpay-googleplay-policy.md §3.7`).
- **Trạng thái**: ✅ Accepted.

## DEC-028 — ReportTargetType: 4 value; PT map sang User

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: Mobile có 4 (`post|comment|user|certificate`), web mock 3 (`post|user|pt`). PT là một vai trò của User, không tạo targetType riêng.
- **Quyết định**:
  - **Enum canonical: `Post | Comment | User | Certificate`** (PascalCase, 4 value).
  - **PT** được report → `targetType=user`, `targetId=userId của PT`. Response có thêm field `target_user_role` (read-only) để admin hiển thị/lọc.
  - **Certificate** report phải trỏ đúng `certificateId`. Phân biệt với luồng nộp/xét duyệt certificate.
  - **Server kiểm tra khi accept report**:
    - Target tồn tại và reporter được phép biết/truy cập.
    - **Không làm lộ certificate riêng tư** qua report API — target visibility check giống Get Certificate.
    - `reason` bắt buộc, giới hạn `minLength=5, maxLength=500`.
    - Rate limit per user: không cho spam.
    - **Idempotency**: không tạo report trùng `Open` từ cùng reporter cho cùng `targetType+targetId` → 409.
  - **Chat message**: nếu release sau cần report message riêng → viết enum `ChatMessage` riêng, **không ép** enum 4 value hiện tại phải cover. Ghi TODO `out-of-scope` để triển khai trước release chat.
  - **Số lượng reports là tín hiệu ưu tiên kiểm duyệt**, không phải bằng chứng tự động khóa tài khoản / xóa nội dung. Admin resolve từng case.
  - **`Block user` là chức năng riêng** (`POST /admin/users/{id}/lock`) — không coi report user đã đáp ứng block.
  - **Auto-actions không có trong release đầu** (ví dụ: N reports → auto-hide) — đó là feature sau cần A/B test.
- **Hệ quả**:
  - OpenAPI `Report.target_type` enum giữ 4 value.
  - `ReportInput.target_type` enum giữ 4 value.
  - `Report` schema thêm `target_user_role` optional.
  - Test case mới: reporter không có quyền xem certificate private → 403.
  - Test case mới: cùng reporter + target + status=Open → 409 conflict.
  - Bảng `reports` thêm `IdempotencyKey` column (nullable cho future-proof).
- **Trạng thái**: ✅ Accepted.

## DEC-029 — PlatformFeeRate: read-only 10% trong release đầu

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: Web có `platformPolicy.platformFeeRate=10` cố định. BE OpenAPI cũ chưa có endpoint quản lý policy.
- **Quyết định**:
  - **Release đầu tiên: read-only**. Phí nền tảng cố định **10%**, lưu chính xác dạng basis points: `platform_fee_rate_bps = 1000`.
  - **`PlATFORM_FEE_BPS`** trong config (appsettings + env override). Không nhận `fee_rate` từ client khi tạo booking/payment — giá trị từ server-side config.
  - **GET `/api/v1/admin/platform-policy`** trả:
    - `platform_fee_rate_bps` (number, e.g. `1000`).
    - `policy_version` (string, e.g. `v1`).
    - `effective_from` (ISO-8601, khi policy có hiệu lực).
    - `display_hint` (read-only text: "10.00% — cố định, chưa hỗ trợ chỉnh từ admin trong release này").
  - **Không tạo endpoint admin tùy ý sửa phí** ở release này. Toàn bộ thay đổi policy phải qua deploy (config + version tăng).
  - **Booking lưu snapshot tỷ lệ/phí theo chính sách áp dụng**: `booking.platform_fee_rate_bps`, `booking.platform_fee_vnd`, `booking.trainer_share_vnd`, `booking.policy_version`. Thay cấu hình tương lai không tính lại booking lịch sử.
  - **Frontend**: convert `1000 bps → "10.00%"` cho hiển thị. OpenAPI ghi rõ đơn vị `basis_points`, kèm `description: "1000 bps = 10.00%"` để tránh nhầm 10 vs 0.10.
  - **Khi scale (post-MVP)**: cần `POST /admin/platform-policy` với permission riêng + audit + version + `effective_from` preview + cool-down. Chưa triển khai ở giai đoạn này.
- **Hệ quả**:
  - OpenAPI: thêm endpoint `GET /admin/platform-policy` + schema `PlatformPolicyResponse` + enum/alias rõ đơn vị.
  - Web `Settings.jsx` label "read-only, 10.00%" đúng — không thêm field sửa.
  - Migration `InitialSchema` không đổi (đã lưu `PlatformFeeRate` integer ở `bookings`).
  - Adapter `Money.PlatFee(priceBilledVnd, bps)` đã có (`Fitmate.Domain/Money.cs`), test cũ 14/14 PASS.
  - Test mới: `GET /admin/platform-policy` → 200 + đúng đơn vị. Test: client gửi `fee_rate` trong booking request → bị ignore (server dùng config).
- **Trạng thái**: ✅ Accepted.

## DEC-030 — Production DB: DigitalOcean Managed Postgres Singapore (mục tiêu)

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: 4 ứng viên managed Postgres đã so sánh. User chọn DO làm baseline kỹ thuật để lấy báo giá và kiểm thử.
- **Quyết định**:
  - **Production target**: DigitalOcean Managed PostgreSQL.
  - **Local**: PostgreSQL Docker Compose (giữ nguyên — đã làm ở phiên 3-5).
  - **Staging**: môi trường tách biệt, không dùng credentials/dữ liệu production. Có thể chạy self-host 1 droplet hoặc 1 managed cluster staging riêng — chốt sau khi có budget.
  - **Region**: ưu tiên Singapore. BE nên đặt cùng region và dùng private networking khi khả dụng.
  - **Cấu hình mục tiêu** (baseline kỹ thuật, không phải cam kết số người dùng):
    - Primary khoảng **2 vCPU / 4 GiB RAM**.
    - **1 standby node** phục vụ HA cùng cấu hình.
    - Storage khởi đầu khoảng **30 GiB** hoặc mức tối thiểu của tier.
    - **Chưa cần read replica** phục vụ đọc.
  - **Phân biệt rõ**:
    - **Standby HA**: phục vụ failover (chỉ chạy khi primary down, không query).
    - **Read replica**: phục vụ read scaling (có thể query, async replication).
    - Hai khái niệm **không thay thế nhau**.
  - **Quy trình SKU/pricing/limits/backup**: xác minh lại trên nguồn chính thức trước khi cấp phát. Điều chỉnh theo load test và ngân sách được duyệt.
  - **Không tự tạo tài nguyên trả phí**. Không chặn việc code BE / migration / integration test local — vì môi trường dev dùng compose Postgres.
- **Hệ quả**:
  - DEC-022 chuyển từ Proposed → **Accepted** với provider + tier + region cụ thể như trên.
  - `managed-postgres-comparison.md` sẽ sửa: bỏ số liệu giá / latency / connection chưa xác minh, bỏ việc dùng secret-leak để loại Supabase, phân biệt Standby vs Read replica.
  - `AGENTS.md` BE cập nhật: local self-host, prod target managed.
  - `.env.example` thêm `DB_SSLMODE=require` (managed bắt buộc TLS).
  - **UA-023 vẫn pending**: cấp credentials qua secret manager, không yêu cầu gửi connection string thật qua chat.
- **Trạng thái**: ✅ Accepted (baseline). UA-023 (cấp credentials) vẫn chờ user khi deploy.

## DEC-031 — Google Play policy: tra cứu lại + mapping, không khẳng định "compliant"

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: UA-024 yêu cầu verify Google Play policy mới nhất. File cũ có một số khẳng định sai (24h xử lý report, auto-block theo N reports, Health Declaration chỉ khi claim chẩn đoán/điều trị).
- **Quyết định**:
  - **Đây là task nghiên cứu + checklist release**, không phải lý do chặn toàn bộ backend.
  - `vnpay-googleplay-policy.md` được rewrite với mapping chính xác theo nguồn chính thức:
    - **PAYMENTS**: Booking PT gặp trực tiếp = dịch vụ vật lý → VNPay OK. **PT online 1:1 KHÔNG được mặc định giống offline** — phải kiểm tra ngoại lệ Google (giữa hai cá nhân + không có bản ghi phát lại trong app). Gói AI / video / digital features phải qua Play Billing. **Không tuyên bố VNPay dùng được cho mọi sản phẩm Fitmate**.
    - **HEALTH**: không ghi "chỉ app chẩn đoán/điều trị mới cần Health declaration". Khai chức năng fitness/dinhưỡng/dữ liệu sức khỏe đúng. Không khai `medical functionality` nếu sản phẩm không có.
    - **UGC**: cần Terms + moderation + report + block user. Có endpoint trong OpenAPI không đồng nghĩa đã đáp ứng — phải có implementation truy cập được. Bỏ khẳng định "Google bắt buộc xử lý report trong 24h" nếu không có nguồn chính thức. Không coi auto-lock theo N reports là yêu cầu Google. Nếu Fitmate chọn SLA 24h nội bộ → ghi đó là chính sách Fitmate, không trích Google.
    - **ACCOUNT DELETION**: có đường xóa trong app + web công khai. Xác định rõ dữ liệu xóa / ẩn danh / giữ lại với lý do hợp lệ. **Không bịa** Google bắt buộc confirmation email hoặc thời hạn purge cụ thể cho mọi app.
    - **DATA SAFETY**: đối chiếu code, SDK, data flow thật. Không mặc định mọi truyền dữ liệu cho provider đều khai giống nhau. Đọc định nghĩa collection/sharing + ngoại lệ chính thức. Không đánh dấu hoàn tất biểu mẫu Play Console khi chưa thao tác.
  - **Outcome**: hoàn thành phần rà soát tài liệu khi có nguồn + mapping. Khai báo / xác minh trong Console là user actions riêng (UA-010..014).
  - **KHÔNG ghi "Google Play compliant"** chỉ từ việc đọc chính sách. Xác minh lại trước ngày submit.
  - Ngày kiểm tra ghi rõ trong file.
- **Hệ quả**:
  - File `vnpay-googleplay-policy.md` viết lại với ngày kiểm tra + 5 link nguồn chính thức user liệt kê.
  - UA-024 đánh dấu done cho "rà soát tài liệu"; giữ "user verify trước submit" là riêng.
  - Không ảnh hưởng code/release/UA khác.
- **Trạng thái**: ✅ Accepted.

## DEC-032 — Bootstrap Identity: dùng ASP.NET Core Identity

- **Ngày**: 2026-09-18
- **Người chốt**: User (qua việc yêu cầu tiếp tục triển khai)
- **Bối cảnh**: BE-021 đang chờ UA-021/UA-018. UA-018 đã chốt; UA-021 đã chốt baseline DO nhưng credentials (UA-023) vẫn pending — không block dev local vì dev dùng Postgres compose. `identity-evaluation.md` đã so sánh Phương án A (Identity) vs B (tự viết) và khuyến nghị A.
- **Quyết định**: triển khai **BE-021** ngay trong phiên này, dùng `ApplicationUser : IdentityUser<Guid>` + 6 bảng Identity (`Users`, `Roles`, `UserRoles`, `UserClaims`, `RoleClaims`, `UserTokens`, `UserLogins`).
- **Hệ quả**:
  - Migration mới: `20260918170000_IdentitySchema` (8 bảng Identity, drop `users`/`user_profiles` cũ hoặc rename).
  - Giữ nguyên `RefreshToken`, `Device`, `AuditLog`, `IdempotencyRecord`, `OutboxMessage`.
  - Refactor `User` → `ApplicationUser` extends `IdentityUser<Guid>` (xem `identity-evaluation.md §4 Phương án A`).
  - CLI `seed-admin` hiện tại dùng `User` entity → refactor sang `ApplicationUser` + `UserManager.CreateAsync`.
  - `dotnet build` solution + `dotnet test` phải xanh trước khi commit.
- **Trạng thái**: ✅ Accepted (BE-021 → IN_PROGRESS ngay trong phiên 7).

## DEC-033 — Không viết nhiều adapter giả

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: AGENTS.md gốc đã có rule "Không fake payment / fake AI trong production. Adapter phải fail khi cấu hình sai". User nhắc lại rõ trong UA-019.
- **Quyết định**:
  - `IPaymentProvider` interface giữ, nhưng chỉ có **1 implementation thật** là `VnpayPaymentProvider`.
  - `FakePaymentProvider` chỉ tồn tại ở `Development` env và được `DI` skip ở tất cả env khác. Production: nếu config thiếu → **fail-fast** lúc startup, không có fallback.
  - Tương tự cho AI (`IAiAdapter`, `IDomain` etc) — chỉ 1 impl thật, 1 fake chỉ dev.
- **Trạng thái**: ✅ Accepted.

## DEC-034 — Dữ liệu cũ không map an toàn → render "—"

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: User quyết định rõ "Không map dữ liệu cũ mơ hồ bằng suy đoán" (UA-018 cuối).
- **Quyết định**:
  - Khi FE mock hoặc data thiếu dữ kiện (ví dụ: mobile có `late` mà BE không có trạng thái tương ứng) → render `"—"` chứ không suy luận mapping.
  - FE adapter trả về enum canonical nếu record đủ dữ kiện; nếu không đủ → trả `null`/`"—"`.
  - Backend không tự chọn default cho field lịch sử không rõ.
- **Trạng thái**: ✅ Accepted.

## DEC-035 — Mock FE chỉ render option BE hỗ trợ

- **Ngày**: 2026-09-18
- **Người chốt**: User
- **Bối cảnh**: UA-019 — web mock có 4 payment method, BE chỉ hỗ trợ 1. User yêu cầu cập nhật integration guide để FE thay mock bằng option thật; không tự sửa FE trong phạm vi BE release.
- **Quyết định**:
  - `mobile-integration-guide.md` bổ sung: payment UI trên mobile chỉ hiển thị "Thanh toán qua VNPay" trong release đầu. Sau release sau mở rộng provider mới.
  - `admin-integration-guide.md` bổ sung: web admin `BookingManagement.jsx` payment filter chip chỉ hiển thị `VNPay` (1 chip), không hiển thị 4 chip như mock hiện tại.
  - Việc sửa FE mock thực tế nằm ngoài scope backend release. Ghi TODO `out-of-scope` trong tasks.md.
- **Trạng thái**: ✅ Accepted.

## DEC-036 — Friend Request dùng `to_user_id`, không dùng email

- **Ngày**: 2026-09-19
- **Người chốt**: User (qua turn trực tiếp, sau khi xem xét 5 lựa chọn thiết kế)
- **Bối cảnh**: BE-181 Friends. OpenAPI draft trước đó dùng `to_email`; email không phù hợp vì (a) user lookup UI không cần trả email, (b) email thay đổi được còn user ID ổn định, (c) privacy — admin/PT khác không nên thấy email để gửi lời mời.
- **Quyết định**:
  - `POST /api/v1/social/friends/requests` nhận `to_user_id` (UUID), bắt buộc.
  - `from_user_id` lấy từ authenticated principal; client không gửi.
  - OpenAPI đổi `to_email` → `to_user_id`. Integration guide cập nhật tương ứng.
  - Tìm kiếm / profile API (FP-ID-12, chưa làm) phải trả public user ID để FE lookup.
  - Server kiểm tra: người nhận tồn tại (`UserExistsAsync`), không phải self, không có block theo cả 2 chiều, chưa là bạn, không có pending request ngược chiều.
- **Hệ quả**:
  - Migration đổi column `FromUserId` → `RecipientId`, `ToUserId` → `SenderId` (PascalCase, EF Core convention).
  - OpenAPI schema `FriendRequest` đổi field names.
  - Không có client production nào gọi `to_email` (mock chỉ) — không cần compat plan.
- **Trạng thái**: ✅ Accepted (BE-181 → IN_PROGRESS).

## DEC-037 — Friendship: 1 row canonical (canonical pair), không 2 row hai chiều

- **Ngày**: 2026-09-19
- **Người chốt**: User
- **Bối cảnh**: BE-181 Friends. User chọn giữa 2-row mutual (A,B) + (B,A) và single canonical `(low, high)`. Quyết định cuối: canonical pair.
- **Quyết định**:
  - Bảng `friendships(user_low_id, user_high_id, ...)` với PK composite.
  - Application dùng `Friendship.CanonicalPair(a, b)` để chuẩn hoá. Quy ước: `low = (a.CompareTo(b) < 0) ? a : b` (.NET `Guid.CompareTo` lexical byte order).
  - DB `CHECK (user_low_id < user_high_id)` — PostgreSQL `<` UUID so sánh value byte order, khớp .NET. Test xác nhận canonical pair khớp giữa application và DB.
  - Index phụ: `(user_high_id)` để query từ cả 2 phía (user có thể nằm low hoặc high).
  - FriendRequest vẫn là bảng riêng có hướng `(SenderId, RecipientId, Status)`.
- **Hệ quả**:
  - Drop table `friends` cũ; tạo `friendships` mới.
  - List friends query: `WHERE user_low_id = @me OR user_high_id = @me` rồi map sang `other_id`.
  - Outbox event `friendship.removed` KHÔNG gửi notification cho người bị unfriend.
- **Trạng thái**: ✅ Accepted (BE-181 → IN_PROGRESS).

## DEC-038 — User Block: bảng riêng, tách khỏi moderation/report

- **Ngày**: 2026-09-19
- **Người chốt**: User
- **Bối cảnh**: BE-181 chốt nhiều policy nghiệp vụ. User chọn triển khai block ngay (không out-of-scope) nhưng tách khỏi moderation, không gộp report.
- **Quyết định**:
  - Bảng `blocked_users(blocker_id, blocked_id, ...)` riêng; 1 chiều (blocker là user chủ động block).
  - Composite PK + index `(blocked_id)`.
  - Khi block: huỷ mọi pending FriendRequest giữa 2 người + xoá Friendship (nếu có). KHÔNG ảnh hưởng booking/finance.
  - Khi unblock: KHÔNG tự khôi phục friendship.
  - Report moderation KHÔNG tự tạo block; UI có thể đề nghị block sau report nhưng là action riêng.
  - Outbox `user.blocked` / `user.unblocked` chỉ cho audit / consumer chưa có. KHÔNG gửi user-visible notification.
  - Feed visibility `friends`: ẩn với người bị block (cả 2 chiều). Friend list filter tương ứng.
  - Block list KHÔNG lộ cho user khác (admin moderation exception chưa chốt — ghi UA-FUTURE).
  - Chat (BE-191 chưa implement): block cản message mới + mở thread mới. Khi chat chưa có, ghi pending integration trong tasks.md, không đánh dấu hoàn thành.
- **Hệ quả**:
  - `block_service` thuộc module Social, không thuộc Moderation.
  - Test block policy: idempotent, không tự khôi phục, huỷ pending request 2 chiều, xoá friendship.
- **Trạng thái**: ✅ Accepted (BE-181 → IN_PROGRESS).

## DEC-039 — Friend/Block outbox event semantics

- **Ngày**: 2026-09-19
- **Người chốt**: User
- **Bối cảnh**: User chốt rõ event nào sinh notification, event nào chỉ audit.
- **Quyết định**:
  - `friend.request.created` → thông báo recipient.
  - `friend.request.accepted` → thông báo sender ban đầu.
  - `friend.request.rejected` / `friend.request.cancelled` → KHÔNG notification.
  - `friendship.removed` → KHÔNG notification cho người bị unfriend.
  - `user.blocked` / `user.unblocked` → KHÔNG notification cho đối tượng bị tác động.
  - Outbox enqueue **cùng `SaveChangesAsync`** với business row (transaction).
  - Consumer retry: kiểm tra block + request status trước khi dispatch. Dedup theo event ID.
  - Worker notification (BE-200) chưa có → ghi pending integration. Service-side outbox vẫn ghi để khi worker sẵn sàng có history. Không báo "đã gửi notification".
  - Outbox delivery ≠ user-visible notification (2 khái niệm tách biệt).
- **Hệ quả**:
  - Application service tự quyết định `Enqueue` dựa trên policy ở trên; không consumer-side filter.
- **Trạng thái**: ✅ Accepted (BE-200 PARTIAL DONE).

## DEC-040 — Push token encryption key ring

- **Ngày**: 2026-09-20
- **Người chốt**: AI (BE-200 Phase 1)
- **Bối cảnh**: Device push tokens are stored encrypted with ASP.NET Core DataProtection. Where does the key ring live?
- **Quyết định**:
  - Dev / Docker Compose: default DataProtection (file-based key ring under app data directory, acceptable for local only).
  - Staging / Production: key ring path overridden via `DataProtectionOptions.KeysPath` + env `DATAPROTECTION_KEYS_PATH` → persistent volume (not ephemeral). See UA-DP-001.
  - Secrets manager (`PersistKeysToAzureKeyVault` / `PersistKeysTo AwsKms`) is deferred to UA-DP-001 (post-MVP).
  - Key rotation: DataProtection auto-rotates keys when `DataProtectionOptions.KeyExpirationPolicy` triggers; existing ciphertexts remain decryptable.
  - The ciphertext carries the key selector, so unprotect works without callers knowing the key id.
- **Trạng thái**: ✅ Accepted (BE-200 PARTIAL DONE — `DataProtectionTokenCipher` + tests committed phiên 27).

## DEC-041 — Push delivery TTL and sweep interval

- **Ngày**: 2026-09-20
- **Người chốt**: AI (BE-200 Phase 1)
- **Bối cảnh**: When should a push delivery attempt stop retrying and be marked Expired?
- **Quyết định**:
  - TTL per notification type (computed from **originating event timestamp**, NOT from retry/defer "now"):
    | Type | TTL |
    |------|-----|
    | FriendRequest | 24 h |
    | System (warn) | 24 h |
    | Booking | 8 h |
    | Payment | 24 h |
    | Reminder | 24 h |
    | (fallback) | 24 h |
  - Sweep interval: every **30 minutes** (`SweepBackgroundService`, `Interval = 30 min`). Claim uses `FOR UPDATE SKIP LOCKED` so multiple instances can run safely.
  - Delivery statuses: `Pending` → (provider sends) `Sent` | (expired TTL) `Expired` | (perm fail) `FailedPermanent` | (token invalid) `TokenInvalid` | (retryable fail) `FailedRetryable` | (deferred) `Deferred` | (sweep) `Sweeping → Expired`.
  - Sweep only processes `Pending` (0), `FailedRetryable` (2), `Deferred` (3) rows.
- **Trạng thái**: ✅ Accepted (BE-200 PARTIAL DONE — `PushDeliverySweepService` + `SweepBackgroundService(30 min)` + 4 tests committed phiên 27).

## DEC-042 — Friend chat canonical pair (UserLowId < UserHighId), tách khỏi AI thread

- **Ngày**: 2026-09-20
- **Người chốt**: User (BE-191 planning)
- **Bối cảnh**: BE-191 Chat (friend message). User chốt mỗi cặp friend chỉ có 1 conversation canonical, không gắn với Friendship cascade.
- **Quyết định**:
  - Bảng riêng `friend_conversations(user_low_id, user_high_id, ...)` với PK composite, `CHECK (user_low_id < user_high_id)`, UNIQUE `(user_low_id, user_high_id)`.
  - Application dùng `FriendConversation.CanonicalPair(a, b)` (Guid.CompareTo lexical byte order) đồng bộ với `Friendship.CanonicalPair` (BE-181/DEC-037).
  - Race 2 request đồng thời tạo conversation → chỉ 1 row (UNIQUE constraint), request thứ 2 retry qua `FindConversationForPairAsync`.
  - **Tách khỏi `ChatThread` AI**: entity `FriendConversation` riêng; `ChatMessage` có thêm nullable `ConversationId` (null = AI thread). Không sửa schema `chat_messages.ThreadId` nullable? — có: alter sang nullable (BE-191 migration `ChatFriendV1`).
- **Hệ quả**:
  - Migration `ChatFriendV1` (2026-09-20): `chat_messages.ThreadId` → nullable; thêm `chat_messages.ConversationId` (nullable, FK conceptual) + `chat_messages.ClientMessageId` (nullable, idempotency); tạo `friend_conversations` + `chat_thread_reads`; indexes (UserHighId, ConversationId+CreatedAt+Id, partial unique ConversationId+ClientMessageId).
  - Không cascade delete: unfriend / block KHÔNG xoá messages.
  - Unfriend → KHÔNG xoá conversation; chỉ chặn gửi message mới (xem DEC-045).
- **Trạng thái**: ✅ Accepted (BE-191 PARTIAL DONE — Domain + Infrastructure + Application committed phiên 28).

## DEC-043 — Friend chat idempotency qua `client_message_id`

- **Ngày**: 2026-09-20
- **Người chốt**: User (BE-191 planning)
- **Bối cảnh**: Mobile retry khi mạng lỗi có thể gửi trùng 1 message. User chốt cơ chế idempotency cho friend chat.
- **Quyết định**:
  - Client cung cấp `client_message_id` (UUID v4) optional trong `POST /chat/friends/messages`. Server lưu vào `chat_messages.client_message_id`.
  - Partial unique index `(conversation_id, client_message_id) WHERE ConversationId IS NOT NULL AND ClientMessageId IS NOT NULL` — chống duplicate row trong cùng conversation.
  - Retry với cùng `(conversation, client_message_id)` + cùng `text` → trả row cũ (200/201 tương đương).
  - Retry với cùng key + khác `text` → `409 FITMATE.CHAT.IDEMPOTENCY_MISMATCH`. KHÔNG âm thầm thay nội dung.
  - Thiếu `client_message_id` → server cấp Idempotency-Key qua header (BE-070 middleware) nếu client cung cấp; nếu không → tạo mới (acceptable vì không có retry semantics ở server level).
  - Outbox event `chat.message.sent` dùng `message_id` làm `source_event_id` + `source_type = "chat_message_sent"` để dedup khi notification worker (BE-200 phase 2) consume.
- **Hệ quả**:
  - OpenAPI: `client_message_id` thêm vào `SendFriendMessageRequest` + response `ChatMessage`.
  - 409 `idempotency_mismatch` có error code `FITMATE.CHAT.IDEMPOTENCY_MISMATCH`.
  - Không đổi `Idempotency-Key` middleware của BE-070 — nó vẫn hoạt động cho mutation tài chính; chat dùng `client_message_id` cho retry.
- **Trạng thái**: ✅ Accepted (BE-191 PARTIAL DONE — committed phiên 28).

## DEC-044 — Read marker monotonic per (conversation, user)

- **Ngày**: 2026-09-20
- **Người chốt**: User (BE-191 planning)
- **Bối cảnh**: User có nhiều thiết bị; cần đảm bảo marker read không bị lùi khi 1 thiết bị đọc cũ hơn thiết bị khác.
- **Quyết định**:
  - Bảng `chat_thread_reads(conversation_id, user_id, last_read_message_id, last_read_at)` với PK composite `(conversation_id, user_id)`.
  - Marker chỉ tiến lên: so sánh `candidate_message.CreatedAt > current_marker.LastReadAt`. Nếu candidate mới hơn → update; nếu cũ hơn hoặc trùng → no-op idempotent (204 No Content).
  - `last_read_message_id` không dùng để so sánh thứ tự (UUID ngẫu nhiên), mà dùng `created_at` của message đó.
  - Mark-read KHÔNG tự động khi `GET .../messages` — client phải gọi endpoint riêng (`POST .../read`) sau khi user thực sự xem.
- **Hệ quả**:
  - OpenAPI: thêm `POST /chat/friends/conversations/{id}/read` body `MarkReadRequest{last_read_message_id}`.
  - Unread count: đếm `chat_messages` where `conversation_id = X AND sender_user_id != viewer AND created_at > viewer_marker.last_read_at`.
  - Tin của chính mình KHÔNG tính vào unread.
  - Race: tin mới đến sau khi marker update → unread tăng đúng (cursor pagination `created_at desc` chứ không phải `id` UUID).
- **Trạng thái**: ✅ Accepted (BE-191 PARTIAL DONE — committed phiên 28).

## DEC-045 — Block / unfriend KHÔNG xoá chat history

- **Ngày**: 2026-09-20
- **Người chốt**: User (BE-191 planning)
- **Bối cảnh**: BE-181 chốt block policy, BE-191 thêm chat. User chốt rõ history được giữ lại.
- **Quyết định**:
  - Block / unfriend KHÔNG xoá `friend_conversations` + `chat_messages` + `chat_thread_reads`.
  - Sau block: cả 2 bên vẫn xem được history (`GET .../messages` của conversation cũ) nhưng KHÔNG gửi message mới (block 2 chiều qua `IsBlockedEitherWayAsync`).
  - Sau unfriend: tương tự — history giữ, KHÔNG gửi mới.
  - Kết bạn lại → dùng lại conversation cũ (cùng canonical pair `(UserLowId, UserHighId)`); `LastMessageAt` cập nhật khi có message mới.
  - Cascade delete KHÔNG được gắn vào `friendships` row.
- **Hệ quả**:
  - Không thêm DB cascade; KHÔNG code xoá messages khi block/unfriend.
  - Authorization matrix §9 cập nhật: `GET /chat/friends/conversations/{id}/messages` vẫn OK sau block (chỉ chặn send).
  - Privacy: cả 2 bên vẫn thấy peer trong conversation list (1 conversation / 1 peer), unread count vẫn cập nhật từ peer side. Admin KHÔNG đọc được content (DEC-005 + domain-rules §5) trừ khi có report → moderation.
- **Trạng thái**: ✅ Accepted (BE-191 PARTIAL DONE — committed phiên 28).
