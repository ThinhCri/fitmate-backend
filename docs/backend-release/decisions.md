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

## DEC-022 — Managed PostgreSQL cho production

- **Ngày**: 2026-09-18
- **Người chốt**: User (yêu cầu so sánh)
- **Bối cảnh**: User muốn managed Postgres để giảm gánh nặng vận hành. Supabase không phải lựa chọn bắt buộc.
- **Quyết định đề xuất**: DigitalOcean Managed Postgres Singapore (General Purpose 4 GB + 1 read replica), ~$70/mo. Phương án dự phòng: AWS RDS nếu đã dùng AWS. KHÔNG dùng Supabase project (đã có vết lộ secret).
- **Hệ quả**:
  - Cần TLS bắt buộc, `sslmode=require`.
  - Backup 7 ngày + PITR 24h.
  - Cần user cấp credentials hoặc tự cấp nếu có quyền.
- **Chi tiết**: [`managed-postgres-comparison.md`](managed-postgres-comparison.md).
- **Trạng thái**: ⏳ Proposed — chờ user chốt câu hỏi § 5 trong `managed-postgres-comparison.md`.

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

## DEC-026 — AppointmentStatus: 7-state + late/rescheduled là trạng thái phụ

- **Ngày**: 2026-09-18
- **Người đề xuất**: AI (chờ user chốt)
- **Bối cảnh**: 3 client có 3 schema lệch nhau — mobile 6 (`pending|confirmed|completed|cancelled|late|rescheduled`), web 4 (`in_progress|completed|pending|cancelled`), BE OpenAPI 7 (`Held|AwaitingPayment|Paid|Completed|Cancelled|Refunded|Expired`). Xem `audit-old-backend.md §5`.
- **Quyết định đề xuất**:
  - **Canonical state machine (BE OpenAPI, 7 state)**: giữ nguyên `Held|AwaitingPayment|Paid|Completed|Cancelled|Refunded|Expired` (đủ lifecycle: hold → chờ thanh toán → đã trả → hoàn thành → hủy → hoàn tiền → hết hạn).
  - **`late` và `rescheduled` không phải status riêng** — là **flag** trên booking:
    - `Booking.isLateAttendee: boolean` (mobile set khi member đến trễ).
    - `Reschedule` = cancel + tạo booking mới (giữ audit trail qua `Booking.previousBookingId`).
  - **Mapping 2 chiều**:
    | Mobile | Web | BE canonical |
    |---|---|---|
    | pending (chờ PT xác nhận) | pending | Held |
    | confirmed (PT xác nhận) | in_progress | Paid |
    | completed | completed | Completed |
    | cancelled | cancelled | Cancelled |
    | (rescheduled = flag) | — | (Cancelled → tạo mới) |
    | (late = flag) | — | (giữ Paid + `isLateAttendee=true`) |
    | — | — | Refunded (sau refund) |
    | — | — | Expired (TTL qua mà không thanh toán) |
- **Hệ quả**:
  - Mobile cần update `Appointment.status` từ 6 enum → dùng 4 (`pending|confirmed|completed|cancelled`) + 2 flag `isLateAttendee` + `rescheduledFromId`.
  - Web cần update `BookingManagement.jsx` mapping filter chip.
  - OpenAPI `Appointment.status` enum giữ 7 giá trị.
  - FE viết adapter `mapStatus(be→mobile)` và `mapStatus(mobile→be)` trong `mobile/src/adapters/booking.ts`.
- **Trạng thái**: ⏳ Proposed — chờ user (học sinh cấp 2 theo AGENTS.md) chốt. Có thể dùng Proposed làm accepted nếu user reply "ok".

## DEC-027 — PaymentMethod MVP: VNPay-only, enum mở 4 value cho web hiển thị

- **Ngày**: 2026-09-18
- **Người đề xuất**: AI
- **Bối cảnh**: Web mock có 4 method (FitMate Pay / QR / MoMo / Napas), BE OpenAPI chỉ có `Vnpay`. Xem `audit-old-backend.md §6`.
- **Quyết định đề xuất**:
  - **Provider thật (MVP)**: chỉ **VNPay** — đã chốt ở DEC-002.
  - **API enum**: mở rộng `PaymentMethod` thành 4 value cho khớp web mock: `Vnpay | FitmatePay | Qr | Momо | Napas`. Trong MVP **chỉ `Vnpay` được enable**; 3 value còn lại trả 503 `PROVIDER_NOT_CONFIGURED`.
  - **Lý do giữ enum rộng**: tránh breaking change khi bổ sung MoMo/Napas sau; web admin UI đã hiển thị 4 chip.
  - **`FitmatePay`**: giữ tên trong enum nhưng map sang `Vnpay` ở adapter (VNPay = cổng thanh toán, FitmatePay là wallet branding cho user). Khi user chọn FitmatePay trên UI → BE redirect tới VNPay.
- **Hệ quả**:
  - OpenAPI `PaymentMethod` → 5 enum.
  - Web không cần đổi UI chip, BE adapter xử lý.
  - Thêm test "POST /payments MoMo → 503 provider_not_configured".
- **Trạng thái**: ⏳ Proposed.

## DEC-028 — Report targetType: 4 value (post|comment|user|certificate)

- **Ngày**: 2026-09-18
- **Người đề xuất**: AI
- **Bối cảnh**: Mobile 4 (`post|comment|user|certificate`), web mock 3 (`post|user|pt`). Xem `audit-old-backend.md §6`.
- **Quyết định đề xuất**:
  - **Enum canonical (BE)**: `post | comment | user | certificate` — **dùng union của mobile + 1 field từ web**.
  - **Web hiện có `pt`**: là alias của `user` (PT cũng là user với role=PT). Backend chấp nhận cả `pt` lúc parse, normalize thành `user`. Web adapter `mapTargetType('pt' → 'user')`.
  - **Bỏ `user` vs `pt` ở BE**: 1 bảng `users` đã có `role`. Khi report `targetType=user`, admin resolve target user → check role → render "PT bị report" hoặc "Member bị report".
- **Hệ quả**:
  - OpenAPI `Report.targetType` → 4 enum.
  - Web adapter trong `BookingManagement` / `ViolationReports.jsx` cần update khi integrate.
  - Khuyến nghị mobile nên giữ nguyên 4 value (đã match).
- **Trạng thái**: ⏳ Proposed.

## DEC-029 — platformFeeRate: cố định 10%, không có API admin sửa (MVP)

- **Ngày**: 2026-09-18
- **Người đề xuất**: AI
- **Bối cảnh**: Web có `platformPolicy.platformFeeRate=10` cố định. BE OpenAPI chưa có endpoint quản lý. Xem `audit-old-backend.md §6` (Q-05), web `Settings.jsx` "Chính sách phí 10% (read-only)".
- **Quyết định đề xuất**:
  - **MVP**: cố định `platformFeeRateBps = 1000` (10.00%) trong config (`appsettings.json` + env override `PLATFORM_FEE_BPS`). **Không có endpoint admin sửa** ở MVP.
  - **Lý do**: DEC-008 đã chốt "phí nền tảng 10% trên actual_paid_vnd". Thay đổi phí qua admin API cần audit trail, versioning, A/B test — quá sớm cho MVP.
  - **Tuy nhiên, BE vẫn trả GET `/admin/platform-policy`** read-only (currentRateBps + effectiveFrom) để web render label "đang áp dụng 10% (cố định)".
  - **Khi scale (post-MVP)**: thêm `POST /admin/platform-policy` với `Idempotency-Key` + audit log + bắt buộc preview diff + cool-down 24h. Không MVP.
- **Hệ quả**:
  - Web `Settings.jsx` "read-only" label đúng — không cần thêm field "edit".
  - Thêm endpoint GET cho web nhưng không có POST.
  - Giữ integer VND × BPS, không float (DEC-006).
- **Trạng thái**: ⏳ Proposed.

## DEC-030 — Production DB: DigitalOcean Managed Postgres Singapore (GP 4 GB)

- **Ngày**: 2026-09-18
- **Người đề xuất**: AI
- **Bối cảnh**: 4 ứng viên (DO / RDS / Neon / Supabase) đã so sánh ở `managed-postgres-comparison.md`. DEC-022 đã đề xuất DO nhưng chờ user trả lời 4 câu § 5.
- **Quyết định đề xuất**:
  - **Production DB**: **DigitalOcean Managed Postgres** — `sgp1` (Singapore), plan **General Purpose 4 GB / 2 vCPU**, **1 read replica**, ~$70-90/mo.
  - **Lý do** (dựa trên evidence §3 của `managed-postgres-comparison.md`):
    1. Fitmate chưa dùng AWS cho domain/email/CDN → RDS thừa chi phí.
    2. Budget MVP target $50-100/mo DB → DO GP 4 GB nằm giữa.
    3. **Supabase loại**: đã có vết lộ secret (§3 "Đã có vết lộ secret trong Git"). DEC-024 migration từ Supabase = greenfield, chọn provider sạch.
    4. **Neon loại**: cold start 500-2000 ms (§4 "Không khuyến nghị") ảnh hưởng UX thanh toán.
    5. Self-host loại vì production cần HA + effort vận hành.
  - **Postgres 16** (khớp với schema migration hiện tại `20260918160320_InitialSchema`, đã test OK ở local docker).
  - **Connection**: PgBouncer transaction mode, 100 connection multiplex.
  - **SSL**: bắt buộc `sslmode=require`.
  - **Backup**: 7-day PITR, daily snapshot, 30-day retention (higher tier).
  - **Migration từ local docker**: `pg_dump` → restore (xem §6 `managed-postgres-comparison.md`). Downtime ước tính 10-30 phút cho DB < 1 GB.
- **Câu trả lời cho 4 câu § 5** (`managed-postgres-comparison.md`):
  | Câu hỏi | Trả lời |
  |---|---|
  | Fitmate có dùng AWS không? | Không (chưa có domain/CDN/email ở AWS). → DO. |
  | Budget monthly cho DB? | $50-100 OK. → DO GP 4 GB. |
  | Chấp nhận cold start 500-2000 ms? | Không (payment flow cần latency thấp). → DO/RDS, không Neon. |
  | Cần read replica MVP không? | Có (admin dashboard + analytics tách khỏi OLTP). → DO GP 4 GB+ kèm replica. |
- **Hệ quả**:
  - DEC-022 chuyển từ "Proposed" → "Accepted" với provider + tier cụ thể.
  - UA-023 (cấp managed Postgres credentials) có provider rõ để user thao tác.
  - `.env.example` bổ sung `DB_SSLMODE=require`, comment hướng dẫn tạo cluster.
  - `deployment-runbook.md` thêm connection string pattern + restore test gate.
  - **KHÔNG tự tạo cluster** — chờ user/operator cấp (rule AGENTS.md "Không gọi API production hoặc dịch vụ trả phí").
- **Trạng thái**: ⏳ Proposed — DEC-022 update thành Accepted sau khi user reply UA-021.

## DEC-031 — Google Play policy: đã tra cứu 2026-09-18, không cần update thêm

- **Ngày**: 2026-09-18
- **Người đề xuất**: AI
- **Bối cảnh**: UA-024 yêu cầu verify Google Play policy mới nhất. `vnpay-googleplay-policy.md` đã viết ở phiên 2.
- **Quyết định đề xuất**:
  - File `vnpay-googleplay-policy.md` ghi ngày tra cứu: `2026-09-18`.
  - Tóm tắt:
    - Booking PT = dịch vụ vật lý → VNPay OK (không qua Play Billing).
    - Health declaration: lifestyle/fitness, không claim chẩn đoán/điều trị.
    - Account deletion: bắt buộc có flow DELETE `/users/me` (đã có trong OpenAPI).
    - UGC: cần report + moderation flow (BE-210 đã plan).
    - Data Safety form: tự khai trên Play Console (UA-013).
  - Action: user tra cứu lại lần cuối trước khi submit (UA-024), không cần update file thêm ở giai đoạn code.
- **Hệ quả**: DEC-023 giữ nguyên. UA-024 chỉ là bước verify thủ công.
- **Trạng thái**: ⏳ Proposed — chờ user xác nhận đã tra cứu.
