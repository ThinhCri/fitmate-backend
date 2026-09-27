# Admin Web integration handoff — 2026-09-27

Docs-only survey theo user; không sửa BE/FE runtime, không build/test/install/commit. Kế hoạch đầy đủ nằm ở repo FitmateWebApp:

- `docs/production/admin-completion/README.md`
- `api-gap-map.md`: routes tìm thấy, G01..G07 và owners.
- `security-integration.md`, `execution-plan.md`, `verification.md`.

## Các điểm BE cần phối hợp (chưa phải kết luận audit toàn hệ thống)

1. BE-803/802/822: AuthController.Login đang check password → IssueTokensAsync; AdminMfaController setup/verify là enrollment. Full admin login MFA challenge và restricted enrollment/session cần kiểm/chốt trước FE production. Web refresh token đang có cả response JSON/cookie; thiết kế flow web không lộ refresh cho JS, bảo toàn mobile contract; cookie-CSRF/Origin/role casing/session invalidation cần runtime evidence.
2. BE-818/810: metrics/summary là uptime/outbox, không đủ 4 business KPIs/time-series. Chốt aggregate/filter/date/timezone endpoints cho Dashboard; không lấy total từ FE page.
3. BE-806/816: public PT directory không thay admin PT pending/rejected/locked; private cert/evidence ownership trước preview/actions.
4. BE-807/809: admin booking list/detail hiện không tự chứng minh đủ timeline/payment/dispute actions; bổ sung DTO/rights khi thiếu, không reuse owner endpoint để né quyền.
5. BE-809/810/816: manual settlement/refund có core code; receipt lookup, evidence ACL, adjustments/ledger và concurrent invariants vẫn phải nghiệm thu. Chưa chứng nhận production từ unit pass.
6. BE-814/818: report detail/evidence/audit query phải contract cụ thể; AuditLog entity không đồng nghĩa có API listing an toàn.

Người triển khai xác minh source mới trước sửa: mỗi gap cần route/DTO/auth/service/test + OpenAPI + client guide. Không tự tạo task IDs hoặc đổi phí10%, không bật auto payout. Gaps đã có implementation mới thì ghi evidence thay vì duplicate code. User chạy mọi install/build/test/migration; không đưa secrets vào docs.
