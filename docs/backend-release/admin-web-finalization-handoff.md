# Admin Web finalization — Backend handoff, 2026-09-27

## Scope và quyền thực thi

User yêu cầu viết tài liệu chi tiết để Antigravity tiếp tục, **không sửa thêm code trong lượt bàn giao này**. Draft code từ lượt trước giữ nguyên, chưa build/test. Không commit, chạy dependencies, Docker, migration, server hoặc giao dịch thật.

Bộ thực thi chính ở repo Web: [finalization README](../../../FitmateWebApp/docs/production/admin-completion/finalization/README.md). Đọc cả 01–07 trong đó. Đây là phần chi tiết hóa backlog hiện có, không tạo task ID hoặc release approval mới.

## Tình trạng cần biết

Sau static review đã có lượt code dở: AuthController/MFA/JWT/refresh/Program; certificate preview/revoke; AdminReview service/store/controller. Web có auth, finance, certificate/support adapters và UI draft. **Không có bằng chứng chạy xác nhận những thay đổi này.** Entry handoff static review cũ “No runtime code changed” chỉ mô tả thời điểm trước lượt draft.

Không coi các endpoint mới đã production-ready. Tests/source contract chưa đồng bộ đầy đủ. Không rollback user changes để quay lại review cũ. File AI/ untracked là dữ liệu user, không xóa hoặc đưa vào phạm vi này.

## Backend work packages

| Backlog liên quan | Việc cần làm | Tài liệu chi tiết Web |
|---|---|---|
| BE-801/820 | Controller DTO ↔ OpenAPI ↔ adapter ↔ tests; enums/pagination/error contracts | 05-contract-execution |
| BE-802/803 | Admin MFA challenge/session proof, refresh owner và atomic rotation, web cookie/native body, security stamp/session revocation | 02-auth-security |
| BE-809/810/818 | Eligibility xét chi lần đầu, manual payout/refund transaction, dispute/refund guard, idempotency concurrent/retry | 03-finance |
| BE-806/816 | Certificate ownership/private preview, paged list, revoke eligibility, atomic audit | 04-admin-workflows |
| BE-807/818 | Admin booking detail/history/cancel và case liên kết, không dùng participant API giả quyền | 04-admin-workflows |
| BE-819/824 | Support detail/status, audit metadata allowlist, metrics/DLQ contract/redaction/replay | 04-admin-workflows |
| BE-826/827 | User verification/staging/operator release gates | 06-verification-operator |

Các mapping là nhóm công việc liên quan; đọc task scope hiện tại để cập nhật đúng vị trí, không đổi ID/đánh DONE toàn task chỉ vì làm xong phần Admin.

## Đặc biệt không bỏ qua

1. RefreshToken.DeviceLabel do client ảnh hưởng, không dùng marker `admin-mfa:v1` làm chứng cứ MFA. Target session/challenge trong 02 là thiết kế cần triển khai, không phải schema đã có.
2. Token setup chỉ enrollment, không được gọi API Admin thông thường. Policy ở server; không chỉ so sánh path trong middleware hoặc guard React.
3. Refresh rotation phải atomic khi hai request cùng dùng token. Logout thường thu hồi current session; logout-all/password/MFA change theo policy riêng, không vô tình revoke mọi thiết bị ở logout thông thường.
4. FinanceStepUp hiện yêu cầu TOTP cả hold; mọi caller phải gửi. Kiểm tra middleware cache 4xx khiến sai TOTP bị replay mãi; không giải bằng bỏ step-up.
5. Settlement approve/transfer phải kiểm tra lại DB state/payment/refund/dispute trong transaction. Idempotency cache check-then-store không tự đảm bảo chống race.
6. Certificate save và audit riêng hiện có nguy cơ mutation thành công mà audit thất bại. Audit cùng transaction; preview cũng có access audit an toàn.
7. `/admin/bookings/{id}/review`, `/admin/trainers/{id}/review`, `/admin/audit` là draft mới. Collections phải bounded/paged khi có thể tăng; không expose raw metadata/PII.
8. Migration chỉ khi schema thật thay đổi; có Up/Down, đánh giá dữ liệu cũ và rollback. User tự apply trên DB test; không generate migration rỗng hay xóa DB để test pass.

## Deliverables phía BE

- Source nhất quán theo tầng, contracts/spec và tests source cập nhật.
- Handoff ghi files, migration implications, native/mobile compatibility, phần chưa làm.
- Evidence chưa chạy ghi IMPLEMENTED_UNVERIFIED; không suy ra PASS từ test cũ.
- Exact PowerShell commands dựa config thực tế để user chạy; không thực thi thay user.
- Operator secrets/contact/merchant/domain/backup/release approval giữ thành gate riêng.

Phần code đang dở chưa đủ để khẳng định Web đã xong hoặc BE production-ready. Bộ docs là đường hoàn tất, không là báo cáo nghiệm thu.
