# Cùng storage R2 cho app local và bản Google Play

Trạng thái: sửa source; owner tự chạy local rồi mới build/deploy. Không tự khởi động app/backend hoặc gọi provider trong lượt sửa này.

## Nguyên nhân trong source và cách sửa

- `compose.vps.yml` từng ép `STORAGE_BACKEND: local`, ghi đè `.env`. Đã chuyển thành `${STORAGE_BACKEND:-local}`. Default Compose local đã đọc biến này. Khi dùng R2 thì cả hai `.env` phải có `STORAGE_BACKEND=r2` và đúng 4 biến R2; không đưa key vào AAB.
- App từng gửi nguyên URL có chữ ký vào `media_url`, backend lưu/trả nguyên URL đó. URL có thể hết hạn trong khi object R2 vẫn còn. App/backend mới chuẩn hóa ảnh trong namespace thư viện chung thành `storage://exercise-catalog/<slug>/0.jpg`; backend ký lại khi trả plan create/update/detail. Kế hoạch cũ được khôi phục mã ảnh từ đường dẫn catalog trong URL đã lưu rồi ký mới khi đọc. Không cần migration/xóa DB/upload lại.
- Chỉ re-sign namespace thư viện chung với slug an toàn và file 0.jpg/1.jpg. Không dùng quy tắc này cho certificates/posts/chat hay đường dẫn tùy ý: media riêng cần kiểm quyền riêng. Không dùng mất chữ ký cũ để cấp quyền mới lên media riêng.
- Thư viện `/exercises` vốn ký ảnh theo storage provider hiện tại. Ảnh kết thúc lấy từ thư viện trong app. App có cache ảnh và nút tải lại lấy URL mới nếu buổi tập kéo dài quá hạn URL. Không thay R2 bằng ảnh giả/local fallback.
- `/system/capabilities.storage_provider` giờ phản ánh `StorageOptions.Backend`, không luôn báo `local`.

## Cấu hình owner đối chiếu

```dotenv
STORAGE_BACKEND=r2
R2_ACCOUNT_ID=<account chứa bucket hiện tại>
R2_ACCESS_KEY_ID=<key có quyền đọc/ghi theo chức năng app>
R2_SECRET_ACCESS_KEY=<secret>
R2_BUCKET_NAME=<bucket đã chứa ảnh>
```

Local và VPS phải dùng đúng bucket/key ảnh, nhưng không sao chép toàn bộ `.env`: DB, JWT, hostname và email test/production là các cấu hình riêng của từng môi trường. `STORAGE_PUBLIC_BASE_URL` không quyết định endpoint R2 presigned URL; không chữa lỗi R2 bằng cách thay nó sang localhost. Không bật public bucket hay tải lại thư viện trước khi kiểm tra lỗi thật.

VPS phải dùng backend đã cập nhật và Compose không còn ép local. Script deploy-mobile-update.sh (operator tự chạy khi duyệt) giữ backup compose và chỉ sửa dòng legacy ép local, không thay toàn bộ cấu hình production. File source.tgz cũ phải chuẩn bị lại sau commit. Lượt này không chạy script.

## Checklist owner test local

1. Chạy backend theo cấu hình R2 hiện tại; kiểm `/api/v1/system/capabilities` báo `storage_provider=r2`. Dùng user test gọi `/api/v1/exercises`; ảnh đầu/cuối phải có URL R2 và GET thành công.
2. Tạo kế hoạch mới, lưu/mở lại; `media_url` trả về là URL đọc mới. DB lưu identity `storage://...`, không cần lưu chữ ký tạm.
3. Mở kế hoạch đã tạo từ bản app cũ; đường dẫn `exercise-catalog/.../0.jpg` trong URL hết hạn phải được backend ký mới. Chờ vượt TTL rồi mở lại để kiểm tra. Không đăng URL ký hoặc key lên chat/log công khai.
4. Tải ảnh trước khi chuyển tab, quay lại phải không nhấp nháy tải toàn màn. Nếu refresh lỗi, vẫn giữ dữ liệu hiện có và báo nhỏ; đổi tài khoản không được thấy dữ liệu người trước.
5. Nếu vẫn lỗi, phân biệt object không tồn tại (404) với chữ ký/quyền (403), bucket sai, device không tới được API hoặc URL localhost. Chưa có kiểm tra object R2 thực tế trong lượt này nên không khẳng định object mất.

Chỉ sau khi local đạt, owner mới build AAB, triển khai backend/Compose và env tương ứng trên VPS rồi kiểm lại từ điện thoại. AAB không chứa R2 secret; người dùng truy cập cùng backend production qua HTTPS.

## Kiểm thử tự động — 2026-10-28 (owner đã cho phép chạy)

- Backend unit: 1072 passed, 0 failed, 0 skipped.
- Mobile: TypeScript passed; 45 test files, 242 tests passed (gồm contract drift và media reference).
- Integration project build: 0 warnings/errors. Chưa chạy integration với PostgreSQL hoặc kiểm thử R2/FCM/VietQR thật; build không thay thế nghiệm thu các luồng này.
- Không tự mở emulator/app, không deploy VPS hoặc build AAB.

## Chạy local thủ công (PowerShell)

Giữ `.env` hiện có; không chép đè bằng `.env.example`. Mở Docker Desktop trước.

```powershell
cd D:\Ky8\exe201\FitmateAI_BE
docker compose up -d postgres
docker compose --profile migrate build migrate
docker compose --profile migrate run --rm migrate
# Chỉ tiếp tục nếu migration exit code 0.
docker compose up -d --build api
Invoke-RestMethod http://localhost:8080/health/ready
Invoke-RestMethod http://localhost:8080/api/v1/system/capabilities
```

Nếu BE `.env` dùng SMTP MailHog (`Email__SmtpHost=mailhog`), thêm `docker compose up -d mailhog`. MailHog không gửi ra email thật; để test email thật cần SMTP thật. API_PORT trong `.env` nếu khác 8080 thì thay port ở lệnh kiểm tra và URL mobile cho khớp. Không xóa volume hoặc đổi Compose project để chữa lỗi.

Mở Android Studio → Device Manager → tự bật emulator. Trong PowerShell mới:

```powershell
cd D:\Ky8\exe201\FitmateAI_FE\mobile
$env:EXPO_PUBLIC_API_BASE_URL='http://10.0.2.2:8080/api/v1'
npm run android
```

Lệnh trên tự build/cài debug và mở app vì owner chủ động chạy. Cần Android SDK/JDK của Android Studio sẵn trong môi trường. Dùng native debug app, không dùng Expo Go để nghiệm thu Firebase. Nếu Metro cũ vẫn đang mở, dừng bằng Ctrl+C rồi chạy lại để nhận URL mới. `.env.local` nếu owner tự có cần kiểm tra tránh trỏ nhầm; biến shell dùng riêng cho phiên test.

Điện thoại thật: thay 10.0.2.2 bằng IPv4 LAN của laptop, cùng Wi-Fi và cho phép port API qua firewall; localhost trên điện thoại không phải laptop.

## Local và VPS: cùng hành vi, khác hạ tầng

| Cấu hình | Quy tắc |
| --- | --- |
| Backend và mobile | Cùng source revision đã test; VPS phải rebuild image, không chỉ restart container cũ. |
| R2 | STORAGE_BACKEND=r2; cùng account/bucket chứa catalog. Key có thể khác nhưng phải đủ quyền. Key chỉ ở BE. Dữ liệu user upload lúc test vào cùng bucket là dữ liệu thật. |
| FCM/VietQR/AI | Cùng provider, các Enabled/feature gates và cấu hình cần thiết; đủ key chưa chắc đã bật capability. Không bật gói chưa có provider. |
| API mobile | Emulator HTTP 10.0.2.2:port/api/v1; AAB HTTPS domain API production thực tế, có /api/v1. Không dùng domain landing page nếu không route API. |
| ASPNETCORE_ENVIRONMENT | Local Development; VPS Production. Không bật Development trên VPS. |
| Database/JWT | DB và secret riêng, không copy DB production về local; giữ JWT/key ring hiện có trên VPS. Không reset volume. SSL theo DB thực tế, managed DB bắt buộc require. |
| CORS | Web local dùng origin localhost; VPS dùng HTTPS domain admin thực tế. Native app không dùng CORS như browser. |
| OAuth/payOS/email links | URL domain HTTPS trên VPS; redirect URI phải đăng ký trùng chính xác với provider. Webhook ngoài Internet không gọi được localhost. SMTP local MailHog khác gửi email thật. |
| Reverse proxy | Local gọi thẳng port API; VPS giữ binding loopback sau HTTPS proxy. Không cần mở PostgreSQL ra Internet. |

Trước AAB: đóng terminal local hoặc Remove-Item Env:EXPO_PUBLIC_API_BASE_URL, đặt EXPO_PUBLIC_API_BASE_URL trong mobile/.env thành API HTTPS production thực tế; giữ google-services.json đúng vn.fitmate.app. Kiểm tra versionCode mới lớn hơn mọi bản từng upload. Sau khi local đạt, triển khai BE/migrations/env tương ứng trên VPS và kiểm tra readiness/capabilities/ảnh thật trước khi phát hành app. Chưa triển khai các bước này trong lượt kiểm thử.

### Lưu ý port thực tế với compose.override.yml
Kiểm tra trực tiếp sau owner khởi động: Compose tự nạp compose.override.yml và API dev đang publish 5116, không phải 8080. GET http://localhost:5116/health/ready trả Healthy; capabilities trả storage_provider=r2. Với cấu hình repo hiện tại dùng URL mobile http://10.0.2.2:5116/api/v1. Khi đổi override/port phải kiểm `docker compose ps` thay vì suy đoán từ API_PORT. VPS dùng compose.vps.yml riêng và domain HTTPS nên không cần đổi theo port local.
