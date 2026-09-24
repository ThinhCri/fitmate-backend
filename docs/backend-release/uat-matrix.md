# Ma Trận Nghiệm Thu Người Dùng (UAT Matrix) — BE-826

- **Mục tiêu**: Hướng dẫn và đối soát kiểm thử nghiệm thu 10 hành trình nghiệp vụ cốt lõi (**BR-01..BR-16**) xuyên suốt 3 kho mã nguồn: **Backend (`FitmateAI_BE`)**, **Android App (`FitmateAI_FE`, thư mục `mobile/`)**, và **Admin Web (`FitmateWebApp`)**.
- **Môi trường mục tiêu**: Staging Environment.
- **Trạng thái ban đầu**: Toàn bộ các hạng mục bắt đầu ở trạng thái `PENDING`. Chỉ chuyển sang `VERIFIED` khi có output chạy thực tế và không còn lỗi P0/P1 mở.

---

## 1. Thông Tin Personas & Quy Tắc Quản Trị Thông Tin Xác Thực

> [!CAUTION]
> **Tuyệt đối không lưu trữ thông tin nhạy cảm trong tài liệu**:
> - Tài liệu này **tuyệt đối không chứa plaintext password, TOTP secret keys, hoặc recovery codes**.
> - Tester và kỹ sư vận hành nhận mật khẩu khởi tạo thông qua kênh bảo mật nội bộ (biến môi trường `UAT_SEED_PASSWORD` hoặc nhập trực tiếp từ STDIN khi chạy seeder).
> - Seeder không bao giờ in mật khẩu hoặc token ra màn hình terminal.

| Định danh Persona (Email) | Vai trò hệ thống | Trạng thái hồ sơ ban đầu | Mục đích kiểm thử UAT |
| :--- | :--- | :--- | :--- |
| `uat.admin@fitmate.local` | `Admin` | Đang hoạt động, hoàn tất MFA TOTP | Quản trị hệ thống, kiểm duyệt PT, đối soát tài chính, Audit Log |
| `uat.pt.verified@fitmate.local` | `PT` | Đã duyệt chứng chỉ, có 2 dịch vụ, 5 slot mở | Khảo sát lịch hẹn, tiếp nhận booking, hoàn thành buổi tập, xem doanh thu |
| `uat.pt.pending@fitmate.local` | `PT` | Chứng chỉ trạng thái `Pending` | Luồng nộp chứng chỉ, kiểm duyệt, từ chối/phê duyệt |
| `uat.member@fitmate.local` | `Member` | Profile hoàn chỉnh, có 1 kế hoạch tập, 1 meal | Đặt lịch PT, thanh toán, ghi nhật ký ăn uống/tập luyện, mạng xã hội, xóa tài khoản |

---

## 2. Bảng Ma Trận 10 Hành Trình Nghiệp Vụ Cốt Lõi

| ID Hành Trình | Nghiệp vụ chuẩn | Preconditions | Thao tác Mobile (`FitmateAI_FE`) | Thao tác Web Admin (`FitmateWebApp`) | API Endpoints tương ứng | Kỳ vọng UI / API / DB | Trạng thái Kiểm Thử |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **UAT-01** | Đăng nhập, Profile & Phân Quyền (BR-01) | Persona Member đã được seed | Đăng nhập Member, cập nhật Theme, Ngôn ngữ, Timezone tại Cài đặt | Không áp dụng | `POST /api/v1/auth/login`<br>`PUT /api/v1/users/me/preferences`<br>`GET /api/v1/admin/users` | Profile lưu DB PostgreSQL; Member gọi endpoint Admin bị chặn `403 Forbidden`; Hết hạn phiên trả về `401`. | - Backend API: `VERIFIED`<br>- Mobile: `PENDING_STAGING_EXECUTION`<br>- Admin: `N/A`<br>- Overall: `IN_PROGRESS` |
| **UAT-02** | PT Onboarding & Kiểm Duyệt Chứng Chỉ (BR-03, BR-04) | PT Pending nộp chứng chỉ | PT xem trạng thái chứng chỉ đang "Chờ duyệt" (Pending) | Admin vào mục "Duyệt PT", xem ảnh chứng chỉ, nhấn "Phê duyệt" | `POST /api/v1/trainers/me/certificates`<br>`POST /api/v1/admin/moderation/certificates/{id}/approve` | Chứng chỉ chuyển `Approved`; PT mở được dịch vụ (`/trainers/me/services`) và slot lịch hẹn. | - Backend API: `VERIFIED`<br>- Mobile: `PENDING_STAGING_EXECUTION`<br>- Admin: `PENDING_STAGING_EXECUTION`<br>- Overall: `IN_PROGRESS` |
| **UAT-03** | Đặt Lịch, Thanh Toán & PT Xác Nhận (BR-05, BR-06, BR-07) | PT Verified có slot mở, Member có số dư | Member chọn slot -> Giữ chỗ -> Thanh toán giả lập qua VNPay Sandbox | Không áp dụng | `POST /api/v1/bookings/holds`<br>`POST /api/v1/payments`<br>`POST /api/v1/me/bookings/{id}/confirm` | Hold chuyển sang `Paid`; PT nhận thông báo và nhấn "Chấp nhận" -> chuyển sang `Confirmed` (State Machine chuẩn). | - Backend API: `VERIFIED`<br>- Mobile: `PENDING_STAGING_EXECUTION`<br>- Admin: `N/A`<br>- Overall: `IN_PROGRESS` |
| **UAT-04** | Thực Hiện Buổi Tập & Doanh Thu PT (BR-07, BR-11) | Booking ở trạng thái `Confirmed` | Đến giờ hẹn, PT nhấn "Bắt đầu" -> "Hoàn thành buổi tập". PT vào mục Doanh thu | Không áp dụng | `POST /api/v1/me/bookings/{id}/start`<br>`POST /api/v1/me/bookings/{id}/complete`<br>`GET /api/v1/me/finance/overview` | Buổi hẹn chuyển `Completed`; Doanh thu PT ghi nhận đúng 90% (trừ 10% phí Fitmate); Đếm đúng số buổi hoàn thành. | - Backend API: `VERIFIED`<br>- Mobile: `PENDING_STAGING_EXECUTION`<br>- Admin: `N/A`<br>- Overall: `IN_PROGRESS` |
| **UAT-05** | Hủy Lịch & Quyết Toán Hoàn Tiền (BR-06, BR-11) | Booking đã thanh toán trước giờ quy định | Member hủy buổi hẹn với lý do hợp lệ (> 5 ký tự) | Admin vào "Hoàn tiền & Quyết toán", kiểm tra số dư và duyệt refund | `POST /api/v1/me/bookings/{id}/cancel`<br>`POST /api/v1/admin/finance/refunds` | Trạng thái chuyển `Cancelled` -> `Refunded`; Sổ cái (Ledger) ghi nhận hoàn tiền; Doanh thu thuần của PT khấu trừ 90% phần refund. | - Backend API: `VERIFIED`<br>- Mobile: `PENDING_STAGING_EXECUTION`<br>- Admin: `PENDING_STAGING_EXECUTION`<br>- Overall: `IN_PROGRESS` |
| **UAT-06** | Kế Hoạch Tập & Đồng Bộ Ngoại Tuyến (BR-08) | Member đang ở màn hình tập | Tạo bài tập -> Tắt kết nối mạng (Airplane mode) -> Hoàn thành buổi tập -> Bật lại mạng | Không áp dụng | `POST /api/v1/me/workout/plans`<br>`POST /api/v1/me/workout/sessions` (kèm `Idempotency-Key`) | Thiết bị gửi lại request với cùng `Idempotency-Key`; Backend xử lý an toàn không tạo bản ghi buổi tập trùng lặp. | - Backend API: `VERIFIED`<br>- Mobile: `PENDING_STAGING_EXECUTION`<br>- Admin: `N/A`<br>- Overall: `IN_PROGRESS` |
| **UAT-07** | Nhật Ký Dinh Dưỡng & Xóa Bữa Ăn (BR-09) | Member có bữa ăn mẫu | Ghi nhận bữa ăn (Phở, Cơm) với portions/macros -> Nhấn xóa bữa ăn | Không áp dụng | `POST /api/v1/me/nutrition/meals`<br>`DELETE /api/v1/me/nutrition/meals/{id}` | Bữa ăn bị xóa khỏi danh sách và database; Dashboard cập nhật lại tổng calo ròng tương ứng; Outbox lưu vết sự kiện. | - Backend API: `VERIFIED`<br>- Mobile: `PENDING_STAGING_EXECUTION`<br>- Admin: `N/A`<br>- Overall: `IN_PROGRESS` |
| **UAT-08** | Bạn Bè, Chat & Cô Lập Chặn 2 Chiều (BR-12, BR-13) | 2 tài khoản Member A và B | Member A kết bạn và chat với B -> Sau đó Member A chọn "Chặn người này" | Không áp dụng | `POST /api/v1/chat/friends/messages`<br>`POST /api/v1/social/blocks` | Chặn hai chiều: A và B không thể nhắn tin hay thấy bài của nhau; Push token của thiết bị khác không bị ảnh hưởng. | - Backend API: `VERIFIED`<br>- Mobile: `PENDING_STAGING_EXECUTION`<br>- Admin: `N/A`<br>- Overall: `IN_PROGRESS` |
| **UAT-09** | Quản Trị MFA, Audit Log & Quyết Toán PT (BR-14, BR-15) | Admin đã thiết lập TOTP | Không áp dụng | Đăng nhập Admin yêu cầu mã 6 số TOTP; Tra cứu Audit Log; Thực hiện quyết toán cho PT kèm mã đối soát ngân hàng | `POST /api/v1/admin/auth/mfa/verify`<br>`GET /api/v1/admin/audit-logs`<br>`POST /api/v1/admin/finance/settlements` | Xác thực MFA thành công; Lịch sử thao tác ghi nhận đầy đủ IP/actor; Quyết toán PT lưu đúng `ReferenceNumber` ngân hàng. | - Backend API: `VERIFIED`<br>- Mobile: `N/A`<br>- Admin: `PENDING_STAGING_EXECUTION`<br>- Overall: `IN_PROGRESS` |
| **UAT-10** | Xuất Dữ Liệu, Thu Hồi Push & Capabilities (BR-16, BR-17) | Member đăng nhập trên thiết bị | Member yêu cầu "Xuất dữ liệu cá nhân" -> Yêu cầu "Xóa tài khoản" -> Đăng xuất và đăng nhập tài khoản khác | Không áp dụng | `POST /api/v1/users/me/export`<br>`POST /api/v1/users/me/account-deletion-request`<br>`GET /api/v1/system/capabilities` | Xuất dữ liệu trả về profile hoàn chỉnh; Device token của tài khoản cũ bị thu hồi tránh lộ push; Capabilities trả về VIP/Reward=Disabled. | - Backend API: `VERIFIED`<br>- Mobile: `PENDING_STAGING_EXECUTION`<br>- Admin: `N/A`<br>- Overall: `IN_PROGRESS` |

---

## 3. Các Tình Huống Ngoại Lệ & Chốt Chặn Kỹ Thuật (Edge Cases)

1. **Thao tác nút Back & Double Submission**:
   - Khi nhấn nút "Thanh toán" hoặc "Lưu buổi tập", nhấn liên tiếp 2 lần hoặc nhấn nút Back trình duyệt/hệ điều hành.
   - Cơ chế `IdempotencyMiddleware` đảm bảo request thứ hai trả về kết quả lưu trữ hoặc bị chặn với lỗi đang xử lý, không trừ tiền hai lần.
2. **Mạng chập chờn (Network Timeout & Offline Reconnect)**:
   - Client mất kết nối giữa lúc request đang bay; khi có mạng trở lại, client retry với cùng header `Idempotency-Key`.
   - Backend nhận diện key đã xử lý thành công và trả về dữ liệu ban đầu mà không sinh bản ghi mới.
3. **Đổi tài khoản trên cùng thiết bị (Push Token Account Switch)**:
   - User A đăng xuất trên thiết bị X. User B đăng nhập vào thiết bị X.
   - Khi User B đăng ký device token `token_fcm_xyz`, hệ thống tự động thu hồi (`IsActive = false`) token đó của User A, ngăn chặn hoàn toàn việc User A nhận push thông báo của User B.
4. **Hết hạn phiên làm việc (Session Expiration)**:
   - Access token JWT hết hạn (sau 15 phút). Client tự động gửi `refresh_token` qua `/api/v1/auth/refresh` để nhận access token mới mà không làm gián đoạn trải nghiệm người dùng.
   - Nếu `refresh_token` đã bị thu hồi do đổi mật khẩu hoặc đăng xuất, client lập tức điều hướng về màn hình đăng nhập.

