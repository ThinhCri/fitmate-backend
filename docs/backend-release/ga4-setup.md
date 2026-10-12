# Google Analytics 4 / Firebase cho Fitmate

Google Analytics đo mức độ sử dụng ứng dụng; số tài khoản, giao dịch và doanh thu của Fitmate vẫn lấy từ Backend, không lấy từ GA4. Không có Firebase project thì Admin hiển thị **Chưa cấu hình**, không hiện số 0 giả. Không dùng Firebase Auth/Firestore thay hệ thống hiện tại.

## 1. Tạo nguồn dữ liệu Android

1. Trong Firebase Console, tạo project Fitmate và bật Google Analytics; chọn/tạo GA4 property.
2. Thêm Android app với package **`vn.fitmate.app`** (phải trùng `mobile/app.json`). Tải **`google-services.json`** vào thư mục `FitmateAI_FE/mobile/`. File này được `.gitignore` bỏ qua. Đây là cấu hình client, **không phải** service-account JSON/private key.
3. Đặt `FITMATE_FIREBASE_ANDROID_FILE=./google-services.json` trong môi trường build mobile. `mobile/app.config.js` chỉ bật native plugin khi file tồn tại; thiếu file sẽ báo lỗi build rõ ràng. Phải tạo lại native Android build sau khi thêm Firebase; Expo Go/ấn `r` không thêm được native module.
4. Analytics mặc định **tắt** trong `mobile/firebase.json`. Trên bản native đã cấu hình, người dùng bật “Chia sẻ thống kê sử dụng ẩn danh” trong Cài đặt hệ thống. App chỉ gửi tên nhóm màn hình allowlist, không gửi ảnh, tin nhắn, tên, UUID, dữ liệu sức khỏe hoặc route động. Tắt công tắc sẽ dừng thu thập.

## 2. Cho Backend đọc GA4 Data API

1. Lấy **Property ID dạng số** ở GA4 Admin → Property Settings. `G-...` là Web Measurement ID, **không** dùng ở đây.
2. Trong Google Cloud project liên kết, bật **Google Analytics Data API**. Tạo service account chỉ để đọc báo cáo, thêm email service account vào GA4 Property Access Management với vai trò Viewer.
3. Tải JSON key cho service account. Chỉ lưu ở máy chủ/secret manager. Trên PowerShell local, tạo Base64 bằng `[Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\path\to\service-account.json'))`; không dán chuỗi này vào chat, source, `app.json` hoặc biến `EXPO_PUBLIC_*`.
4. Đặt `Ga4__PropertyId=<số>` và `Ga4__ServiceAccountJsonBase64=<Base64>` trong `.env` backend hoặc secret manager, rồi **khởi động lại Backend**. Admin → Tổng quan → Google Analytics 4 đọc báo cáo 7/30/90 ngày, cache 5 phút. Backend chỉ dùng scope `analytics.readonly`.

## 3. Kiểm tra nghiệm thu

- Không cấu hình: `GET /api/v1/admin/analytics/overview` với token Admin trả `configured=false`, các chỉ số `null`; Member bị 403, chưa đăng nhập bị 401.
- Cấu hình sai/quyền thiếu: trả 503, không thay bằng số 0.
- Bản Android native: bật chia sẻ thống kê trên một máy thử, mở các tab. Để xem DebugView trên Android debug, chạy `adb shell setprop debug.firebase.analytics.app vn.fitmate.app`, rồi mở Firebase Analytics DebugView. Đối chiếu sự kiện `screen_view` và tên nhóm màn hình; kiểm tra sau khi tắt công tắc không còn gửi sự kiện mới. Google Analytics báo cáo tổng hợp có thể cập nhật chậm hơn DebugView.
- Admin Web không tự gửi traffic quản trị vào cùng Android app stream, để không làm sai chỉ số người dùng app. Web chỉ đọc báo cáo GA4 qua Backend.

Không có key hoặc build native thì **chưa thể nghiệm thu gửi/đọc sự kiện GA4 thật**. Đây là bước cấu hình bên ngoài, không phải dữ liệu mock.
