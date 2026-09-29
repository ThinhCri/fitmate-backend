# Authorization — production baseline
PROD-2026-09-22-v1. Enforce Application + object scope. PT kế thừa Member trên dữ liệu riêng; Admin không mặc định đọc private health/chat.
404 resource private không thuộc caller; 401 thiếu auth; 403 thiếu role/capability; 409 conflict.

| Action | Public | Member | PT | Admin |
|---|---|---|---|---|
| Register/login/verify/reset | Rate limited | Own | Own | Login/reset + MFA; không public register admin |
| Marketplace public fields | Read | Read | Read | Read |
| Profile/extended/settings/weight/meals/workout | No | Own | Own | Own basic profile, không raw health người khác |
| Search/public profile | No | Minimal verified scope | Same | User management allowlist |
| Cert/service/availability writes | No | No | Own, verified để bookable | Cert approve/revoke có reason |
| Hold/pay | No | Own | Own với PT khác | Không giả Member |
| Booking read/cancel/reschedule | No | Participant | Participant | Scoped read, mutation audited |
| Accept/start/complete | No | No | Assigned PT | Case correction có chứng cứ, không arbitrary status |
| Payment read | No | Payer | Payer hoặc PT revenue projection | Finance allowlist |
| Provider callback | Signature/server verification | Không đặc quyền từ login | Same | Same |
| Refund/payout/reconcile | No | Request dispute | Own dispute/settlement read | Finance action + MFA/reason/audit |
| Revenue | No | No | Own | Platform/authorized projection |
| Social/media | No | Visibility + block | Same | Scoped moderation |
| Friend/chat/read | No | Participant/send guard | Same | Không unrestricted inbox |
| Report | No | Visible target | Same | Case actions audited |
| Notification/device | No | Own | Own | Own chỉ khi contract bật; không revoke hộ |
| Upload/signed URL | Public published asset only | Owner + visibility | Same | Assigned cert/report case |
| Delete/export | Verified request path | Own | Own | Own + no last-admin loss |
| Users/reports/audit/metrics | No | No | No | Server filter/sort/pagination allowlist |

Không trả password hash/keys/refresh token/provider raw payload qua profile hoặc list.
Health/chat chỉ owner/participant; case evidence do người có quyền cung cấp, time-bounded và audited.
Certificate approved chỉ public metadata, không public scan/số cá nhân.
Email/phone/birthday không search/feed public. Token device không echo; deep link re-authorize.
Account lock/deletion/revoke kiểm cả phiên đã cấp. Admin list max100; chống mass assignment role/verified/fee.

## Cert image preview ownership (2026-09-28)

Prefix của storage key KHÔNG chứng minh ownership. Kiểm chuỗi cả 2 lớp:

1. **Lúc upload** — `UploadsController` nhúng user id vào key: `{category}/{userIdN}_{file}_{random}{ext}`.
   Key do hệ thống phát ra, không phải do client chọn.
2. **Lúc submit** — `CertificatesController.Submit` kiểm key đó có scope đúng user hiện tại
   không (`ImageKeyBelongsTo`); sai → 403 `CERT_IMAGE_NOT_OWNED`. Nếu bỏ bước này thì PT có
   thể dán key của người khác và làm check "uploader == owner" ở bước 3 trở nên vô nghĩa.
3. **Lúc preview** — `GET /admin/certificates/{id}/preview` kiểm cột `Certificate.ImageUploadedByUserId`
   (người thực sự upload) thay vì suy đoán từ key:

| Điều kiện | Kết quả | Lý do |
|---|---|---|
| `ImageStorageKey` null | 404 | Cert chưa có ảnh |
| `ImageUploadedByUserId` null (dữ liệu trước 2026-09-28) | 404 | Không chứng minh được ai chủ; KHÔNG tự gán `= PtUserId` |
| `ImageUploadedByUserId != PtUserId` | 403 | Ảnh không thuộc PT của cert này |
| Khớp | 200 + audit `certificate.preview.viewed` | Signed URL TTL 300s |

Admin review cert của PT khác vẫn được (list/approve/reject/revoke theo vai trò Admin); chỉ endpoint
phát signed URL mới bị chặn. Backfill `ImageUploadedByUserId` chỉ khi operator có bằng chứng ai upload.
Key cũ (trước khi nhúng user id) không submit lại được — không chứng minh được chủ sở hữu.
