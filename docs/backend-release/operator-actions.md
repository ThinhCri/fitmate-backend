# Operator actions — điều kiện bên ngoài
Baseline PROD-2026-09-22-v1. Không chứa secret. [user-actions.md](user-actions.md) giữ lịch sử UA; bảng này là nhóm gate hiện hành, không tự đóng UA cũ.

| ID | Ai cung cấp/duyệt | Cần làm | Chặn phần nào | Developer vẫn làm được |
|---|---|---|---|---|
| OPS-01 | Owner repo/security | Xác minh rotate secret lịch sử; duyệt riêng rewrite nếu cần; cấp GitHub quyền/policies | History cleanup, CI protection | Scanner, tests và code local |
| OPS-02 | Operator/domain owner | Domain gửi mail, SPF/DKIM/DMARC, SMTP/provider credentials qua secret manager | Email delivery staging | MailHog, outbox, templates và reset logic |
| OPS-03 | Business/finance + operator | VNPay merchant/sandbox/prod; quy trình refund/payout, tài khoản PT, điều khoản fee/cancel/settlement | Thu/hoàn/chi tiền thật | Adapter/tests sandbox khi có creds, ledger và UI |
| OPS-04 | Product/privacy owner | Công bố privacy/terms, retention mỗi loại dữ liệu, quyền sử dụng media, health declarations | Release public | Deletion/export/jobs, private defaults |
| OPS-05 | Operator | Budget + domain/TLS + registry + VPS + DB HA/backup/storage/DP keys; credentials qua secret manager | Provision/deploy staging/prod | Docker, IaC/runbooks, isolated test |
| OPS-06 | Product + mobile/operator | Chọn FCM/Expo và Android config, credentials; team AI giao artifact/schema/license/resources | Push/AI enabled | In-app inbox, unavailable/manual paths, adapters |
| OPS-07 | Play account owner | App ID, signing/upload key, Console access, current target API/testing rules, forms/store assets | Play release | Build config, privacy/deletion public page, tests |
| OPS-08 | Release owner | Ký nhận release manifest, residual risk, incident owner và ngân sách | Production rollout | Chuẩn bị mọi evidence |
Không yêu cầu gửi key/password qua chat hoặc commit. Ghi tên secret reference và owner, không ghi giá trị.
Mặc định sản phẩm mới (acceptance timeout/payout hold/retention targets) phải được review cùng điều khoản trước go-live, không chặn viết code configurable.
