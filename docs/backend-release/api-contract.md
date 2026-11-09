# API conventions — production target
PROD-2026-09-22-v1. OpenAPI không được rewrite hàng loạt trong task docs. BE-801 đối chiếu routes/DTO/spec trước freeze.
Endpoint proposed không được FE coi là đã có.

## IDs, enums, tiền và thời gian
UUID strings thay CUID cũ; email không identity. DTO giữ exact wire naming theo schema đã có consumer.
Additions hướng snake_case cho domain fields; pagination camelCase hiện có ghi explicit, adapter FE chuyển UI shape.
Appointment/payment/refund PascalCase theo domain; day_of_week mon..sun; exercise reps/time.
UTC ISO8601 và timezone riêng cho calendar date. Null không tự thành zero.
Money integer VND/bps, JSON safe integer bounds ghi trong schema, fee do server; không dùng float để tính phí.
Contract tests bắt naming/casing/null/required/date/enum.

## HTTP
GET không mutation. 201 create; replay status theo operation spec; 204 không body.
Private not-owned404, role403, auth401, invalid400, conflict409, rate429 + Retry-After, unavailable503.
ProblemDetails: type/title/status/detail/code/traceId, errors optional; không secret/stack/PII.
Admin page>=1/pageSize default25 max100, total áp cùng filter.
Feed/chat/session/weight cursor stable + next cursor; default/max theo schema, không truncate im lặng.
Date range [from,to); filter/sort allowlist, cap date span/export.

## Auth và retries
Mobile Bearer + refresh body ở secure storage; web access in-memory + refresh HttpOnly Secure cookie, CSRF/Origin/CORS exact.
Cookie flow là target BE-803, không giả đang implement.
FE refresh single-flight; 401 chỉ retry một lần; logout clear per-user cache.
Financial mutation Idempotency-Key; session thêm client_id body.
Request dedup scope actor+operation+key, canonical payload hash. Business client_id unique bền hơn TTL request cache.
Same identity/different payload409; không replay dữ liệu user khác.
Webhook verify provider/merchant/amount/currency/signature; dedup provider txn/event. Redirect không settlement.

## Media
storageKey bền, signed URL tạm; cấp URL sau authz. Không attach object của người khác.
Uploads category/size/magic/type/path allowlist và quota; không key/secret ở Expo/Vite public env.

## Change management
Trước production client đầu: coordinated breaking changes v1 được phép nếu docs/spec/fixtures/generated clients cập nhật cùng.
Sau freeze: additive hoặc version/migration window có consumer agreement.
Mỗi operation có implemented/spec-only/proposed/verified marker trong coverage report.
Production lỗi phải show error/unavailable; không fallback mock.

## Social profile feed follow-up — 2026-10-26
`GET /social/posts` accepts optional `author_id` UUID (empty UUID is 400). Filter is applied before cursor/limit and after the same visibility/friendship/block rules; filtering an author does not grant access. Friends scope only includes friends-only posts of friends, never their private posts. Own private posts remain owner-visible. No public health aggregates added.

Social/friend/block cursors are opaque: new encoding preserves .NET timestamp ticks with a v2 marker, and decoder accepts legacy unix-ms cursors. Clients must pass `nextCursor` unchanged. Invalid/out-of-range/oversized cursors return 400 rather than a server error. Restart pagination if filters change. Unit tests passed; PostgreSQL integration source compiled but local port 5432 unavailable. Generated clients/hash still need normal regeneration/verification before contract freeze.


## Payment/settlement override — 2026-09-26
User chọn tích hợp payOS cho checkout mới và chi PT thủ công sau gọi xác nhận khách + PT + hồ sơ buổi tập. Xem [runbook](runbooks/payos-manual-settlement.md) và handoff mới. Code/migration/tests đã soạn, chưa chạy; evidence cũ không verify thay đổi mới. OPS-03 cần xác nhận provider cho mô hình thu tiền/chi PT trước live. Không tự deploy/commit/chuyển tiền.

## Contract status: FROZEN-V1 — 2026-10-02 (BE-801)
Contract v1 đã được kiểm chứng và đóng băng giữa Backend, Mobile App (`FitmateAI_FE\mobile`), và Admin Web (`FitmateWebApp`). HTTP integration suite đã chạy pass trên PostgreSQL cô lập.
- Method/path parity đạt 100% khớp nối.
- Request/response wire schema parity đã được kiểm chứng qua parser chuẩn OpenAPI.NET và bộ test contract chuyên biệt.
- Wire JSON conventions:
  - snake_case bắt buộc cho tất cả domain/request/response fields;
  - camelCase chỉ được giữ cho pagination metadata đã có compatibility (`nextCursor`, `pageSize`);
  - Tiền là số nguyên VNĐ (`integer`), platform fee rate là basis points (`bps`, 0.10 -> 1000);
  - Timestamp canonical là ISO-8601 UTC; calendar-local input luôn đi kèm timezone riêng và không xuất lại dưới dạng thời điểm mơ hồ;
  - Enum casing đồng bộ theo domain contract (PascalCase hoặc snake_case rõ ràng);
  - Decoupled wire DTOs với `[property: JsonPropertyName("...")]` loại bỏ hoàn toàn việc trả domain entity trực tiếp.
- Mobile App được phép generate client và viết API adapter từ revision/hash được ghi trong evidence BE-801.


Optional compatibility header: X-Fitmate-Android-Version-Code carries the native Android build code. AI subscription status/verification responses hide new sales for builds below GooglePlayBilling.MinimumAndroidVersionCode (minimum5) or missing/invalid header. Restore and server-verified entitlement are preserved; no role or purchase authorization relies on a client-supplied build value.

Admin AI sales: GET /admin/finance/ai-sales is read-only, Admin controller policy plus Application guard. Real/known purchases only; server filter/sort/page and VND Google Orders values. Unknown net revenue is null. No purchase tokens, test rows, buyer addresses or inferred bank payouts. OpenAPI defines response/filter details.

## 2026-11-09 — Buyer-level AI purchase reporting
GET /admin/finance/ai-sales/buyers reuses the admin application guard and bounded server filters (range, search <=200 literal case-insensitive name/email, userId, state, orderId, sort, page/size). Groups real classified orders by Fitmate user/product/base plan. Counts unique Google orders in processed/refund states; pending/canceled and test/unknown purchases are excluded. OrderTotalVnd sums provider totals before refund subtraction, not net spend; RefundedOrders is a count, never an inferred amount. Missing developer revenue stays null. The existing order endpoint now returns nullable display name/email with a left join and supports exact userId history; missing/deleted identity does not erase financial rows. No migration or payment mutation.

### Partial revenue contract
confirmedRevenueVnd is the subtotal of Google-supplied net values, including known refund adjustments. It remains available when another order has null net. revenueUnavailableOrders separately signals incomplete coverage; zero confirmed does not assert missing orders have zero revenue. developerRevenueVnd retains its complete-total/null semantics. Admin and CSV must label confirmed subtotals and missing counts together.

## Admin finance SignalR
GET/POST /api/v1/admin/finance/live uses SignalR negotiation/transports, not a REST report. Only Admin with Application authorization joins server-assigned admin-finance group. Event finance.updated has only scope=ai-sales; browser re-reads authorized report APIs. Outbox emission is committed with order change. On reconnect, refetch current filters because disconnected messages may be missed. No order/customer/payment data goes in event payload. WebSockets use scoped access_token authentication only on the hub prefix; other API routes continue requiring Authorization headers. Configure hub proxy Upgrade/Connection, no buffering, and suppress query-string access logs on this path.

Admin AI sales search <=200 now matches any literal substring of Google order ID OR user display name OR email, case-insensitively, before pagination/aggregation. Existing orderId remains exact for compatibility. '%' and '_' are literal search characters, never user-provided SQL wildcard patterns.


## One-time AI Plus — 2026-11-10
GET /me/ai-subscription supplies purchase_type=in-app and legacy_product_id on Android build8+, product_id=fitmate_ai_plus_month, base_plan_id empty. Older/missing build gets legacy subscription metadata with billing_available=false but unchanged account entitlement/restore. POST /me/ai-subscription/verify-purchase takes purchase_token and Idempotency-Key; authenticates owner; product-level server Google verification + strict obfuscated app-account binding; one token grants exactly one calendar month (stack from user's valid expiry), then server outbox consumes. Token hash is fulfillment deduplication key regardless of retry key. Pending/cancelled, foreign binding or unknown already-consumed token is refused, never silently relinked. Existing verified consumed record remains valid until its server expiry or verified revocation. /verify remains prepaid subscription-only. OneTimeSalesEnabled separate switch remains false until Console/UAT; verification/restore continues after sales close. Full real-order finance uses oneTimePurchaseDetails with no fabricated net.
