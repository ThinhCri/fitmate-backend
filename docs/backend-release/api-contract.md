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


## Payment/settlement override — 2026-09-26
User chọn tích hợp payOS cho checkout mới và chi PT thủ công sau gọi xác nhận khách + PT + hồ sơ buổi tập. Xem [runbook](runbooks/payos-manual-settlement.md) và handoff mới. Code/migration/tests đã soạn, chưa chạy; evidence cũ không verify thay đổi mới. OPS-03 cần xác nhận provider cho mô hình thu tiền/chi PT trước live. Không tự deploy/commit/chuyển tiền.
