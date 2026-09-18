# Đánh giá ASP.NET Core Identity cho Fitmate

> Viết 2026-09-18. Cập nhật khi chốt.

## 1. ASP.NET Core Identity là gì

Framework user management do Microsoft cung cấp cho ASP.NET Core. Mặc định:
- Entity lưu trong 8 bảng: `Users`, `Roles`, `UserRoles`, `UserClaims`, `RoleClaims`, `UserLogins`, `UserTokens`, `Roles`.
- Password hashing qua `PasswordHasher<TUser>` (PBKDF2 với HMAC-SHA256, 100k iterations default).
- Lockout, 2FA, email confirmation, password reset có sẵn.
- Token provider cho reset password / change email / 2FA.
- Tích hợp sẵn với `SignInManager`, `UserManager`, `RoleManager`.

## 2. So sánh với nhu cầu Fitmate

| Nhu cầu Fitmate | ASP.NET Core Identity | Nhận xét |
|---|---|---|
| Register (member, PT) | ✅ Có | Mặc định `Email + Password`. Tùy biến thêm field (full_name, role). |
| Login (email + password) | ✅ Có | Có sẵn qua `SignInManager.CheckPasswordSignInAsync`. |
| Lockout sau N lần sai | ✅ Có | `LockoutEnabled`, `LockoutEnd`. Mặc định 5 attempts / 5 min. Cần chốt policy. |
| Email verification | ✅ Có | `RequireConfirmedEmail = true` + `GenerateEmailConfirmationTokenAsync`. |
| Password reset | ✅ Có | `GeneratePasswordResetTokenAsync`. |
| Refresh token / JWT issuance | ❌ Không có | Phải tự viết. Identity hỗ trợ `UserTokens` lưu nhưng không có khái niệm "refresh + access". |
| Logout / revoke session | ❌ Không có | Phải tự quản lý `RefreshToken` entity. |
| RBAC với Admin/PT/Member | ⚠️ Có role cơ bản | `AddRoles<IdentityRole<Guid>>()` rồi `UserManager.AddToRoleAsync`. OK cho MVP. |
| Custom fields (full_name, timezone, avatar_url) | ⚠️ Cần extend | Phải tạo `ApplicationUser : IdentityUser<Guid>` với thêm field. |
| Object-level authorization (PT chỉ đọc data của mình) | ❌ Không có | Phải tự enforce ở Application layer. |
| Audit log cho hành động nhạy cảm | ❌ Không có | Phải tự viết. |
| Idempotency / Outbox | ❌ Không có | Phải tự viết. |
| Tiền VND integer | ❌ Không liên quan | Tự viết adapter. |
| Mobile-first secure storage | ❌ Không có | Tự viết ở FE (expo-secure-store). |

## 3. Đánh giá khách quan

### Ưu điểm nếu dùng
- Đã có sẵn **password hashing đúng chuẩn** (PBKDF2 HMAC-SHA256) — không phải tự cài BCrypt.
- **Lockout, email confirm, password reset, 2FA** đều có sẵn → giảm code tự viết.
- **Token provider** cho reset password / email confirm dùng được.
- Được maintain bởi Microsoft, vá security thường xuyên.
- Schema chuẩn, có thể migrate user từ hệ thống khác qua `UserManager`.

### Nhược điểm nếu dùng
- **Schema Identity cồng kềnh**: 8 bảng cho chỉ 2-3 use case thực tế (login, register, password reset). Khó map sang `User` aggregate hiện tại (đã có `User`, `UserProfile`, `RefreshToken`, `AuditLog`).
- **Email làm username**: Identity mặc định dùng email. Fitmate đã chốt `Email` làm định danh (xem `domain-rules.md`) → không phải vấn đề.
- **Email confirmation token lưu DB**: Mặc định `email_confirmation_token` plaintext trong DB. Rủi ro nếu DB leak. Cần encrypt hoặc dùng token riêng.
- **Không có refresh token / JWT bearer flow**: Phải tự viết. Nếu tự viết thì sao không tự quản user luôn?
- **Khó debug**: Khi user complain "không login được", nguyên nhân có thể từ Identity middleware, cookie, token provider, role policy, hoặc authorization filter — tăng độ phức tạp.
- **Không phù hợp khi muốn kiểm soát 100% schema và token lifecycle**: Mỗi lần muốn custom (vd: thêm trường, đổi TTL, thêm audit) phải hook vào lifecycle event.

## 4. Phương án đề xuất

### Phương án A — Dùng ASP.NET Core Identity (đề xuất)
- **IdentityUser<Guid>** + IdentityRole<Guid> + 4 bảng phụ (UserRoles, UserClaims, UserTokens, Roles).
- **ApplicationUser : IdentityUser<Guid>** thêm `FullName`, `Timezone`, `Status`, `CreatedAt`, `UpdatedAt`, `DeletedAt`.
- **RefreshToken entity tự viết** (vì Identity không có).
- **JWT bearer + Refresh token** tự viết dùng `UserManager.GenerateUserTokenAsync` (cho reset password) và tự issue JWT access token qua `JwtSecurityTokenHandler`.
- **Audit log + Outbox + Idempotency** tự viết như hiện tại.
- **Authorization** kết hợp role policy của Identity + ownership check tự viết ở Application layer.

### Phương án B — Tự viết toàn bộ auth (như hiện tại đang làm)
- `User` entity + `BcryptPasswordHasher` (custom).
- `RefreshToken` entity + JWT issuance tự viết.
- Ưu: kiểm soát hoàn toàn, ít schema, dễ test.
- Nhược: tự chịu trách nhiệm về best practice (hash strength, timing-safe compare, token entropy, lockout).

## 5. Khuyến nghị

**Phương án A**. Lý do:
1. Lockout, email confirm, password reset cần cho production — Identity đã có và được Microsoft duy trì.
2. Tự viết sẽ phải cover ~500 LOC cho các tính năng Identity đã cung cấp sẵn.
3. Identity `UserManager` có rate-limit hooks và lockout tracker — không phải tự quản.
4. JWT issuance vẫn tự viết (Identity không can thiệp), nên quyền kiểm soát token lifecycle vẫn đầy đủ.
5. Schema 8 bảng Identity chấp nhận được: dùng thêm 4 bảng phụ, nhưng bù lại có sẵn schema phục vụ audit và 2FA sau này.

## 6. Schema cần thêm khi dùng Identity

Bổ sung vào `docs/backend-release/data-model.md`:
- `asp_net_users` (thay cho `users` hiện tại): thêm `normalized_email`, `email_confirmed`, `password_hash`, `security_stamp`, `concurrency_stamp`, `phone_number`, `phone_number_confirmed`, `two_factor_enabled`, `lockout_end`, `lockout_enabled`, `access_failed_count`.
- `asp_net_roles`: id, name, normalized_name, concurrency_stamp.
- `asp_net_user_roles`: user_id, role_id.
- `asp_net_user_claims`: id, user_id, claim_type, claim_value.
- `asp_net_user_tokens`: user_id, login_provider, name, value.
- `asp_net_role_claims`: id, role_id, claim_type, claim_value.
- Giữ nguyên `user_profiles`, `refresh_tokens`, `audit_logs`, `devices` như hiện tại.

## 7. Việc cần làm

- Cập nhật `decisions.md` thành DEC-009 chốt dùng Identity.
- Cập nhật `data-model.md` thêm 4 bảng Identity.
- Cập nhật `audit-old-backend.md`: BE cũ không có schema Identity → migration = greenfield, không có data conversion.
- BE-030 (Auth) sẽ dùng `ApplicationUser : IdentityUser<Guid>`.
