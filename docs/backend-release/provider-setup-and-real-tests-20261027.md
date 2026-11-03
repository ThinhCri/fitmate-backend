# Cấu hình provider và kiểm thử thật — 27/10/2026

Trạng thái: code đã nối provider/API thật; chưa nghiệm thu đầu cuối, chưa deploy lượt này. Đây là bản cập nhật cho hai báo cáo audit ngày 26/10, không phải chứng nhận mọi chức năng đã chạy trên production.

## Đã hoàn thiện trong source

- FCM HTTP v1 thật, service-account OAuth, đăng ký token Android sau cấp quyền, token refresh, thu hồi khi logout/đổi tài khoản. Token chỉ có một chủ đang hoạt động; khóa giao dịch PostgreSQL chống đăng ký đồng thời. Push chỉ chứa thông báo chung, mở hộp thư sau xác thực; không đẩy nội dung riêng tư lên màn hình khóa.
- Outbox lưu delivery, worker retry 15 giây, claim lease 2 phút chống hai worker gửi đồng thời. Trạng thái Sending=8 bổ sung, giữ Expired=7. Sửa SQL sweep dùng Sweeping=6; không thay schema hay xóa dữ liệu. Sau sự cố giữa provider nhận tin và ghi Sent vẫn có thể trùng thông báo; không hứa exactly-once.
- Gemini quét ảnh món ăn thật; Groq gợi ý bài tập thật, dùng quota chat hiện có. Không tự ghi kết quả AI thành bữa ăn/kế hoạch. Không bịa độ tin cậy. Chat chữ dùng Groq; chat ảnh dùng Gemini hoặc trả lỗi thiếu cấu hình rõ ràng, không gửi ảnh vào model chữ.
- Hồ sơ cộng đồng dùng số bài hiển thị/bạn bè thật. Số buổi tập hoàn tất chỉ chủ tài khoản xem được. Huy hiệu cộng đồng là chỉ báo hiện tại từ ít nhất 10 bài mà người xem được phép xem, không phải chứng nhận sức khỏe, điểm hay quyền đổi tiền.
- Đánh giá PT lấy booking thật và đúng PT của booking; không còn ID `bk-2`, không mặc định 5 sao. Dữ liệu trạng thái/thời lượng sai bị từ chối; API lỗi không biến thành số 0/miễn phí.
- Payment test double đã chuyển hoàn toàn sang tests/Shared, không biên dịch trong ứng dụng. `Payment__AllowMockProvider=true` bị từ chối ngay. Adapter chưa cấu hình trả lỗi thật, không giả giao dịch thành công. Bỏ các presentation camera/avatar mẫu không còn caller, bỏ thẻ hứa quà tặng chưa có sản phẩm thực tế.
- Admin dùng adapter API thật. MSW/fixtures chỉ còn trong test; giữ chúng để kiểm thử lỗi/quyền, không import vào runtime.

## Key cần bạn thêm

Chỉ sửa private `/home/fitmate/backend/.env` hoặc secret manager. Không đưa key vào Git, chat, `EXPO_PUBLIC_*` hay bundle Android. Bảng dựa trên audit presence ngày 26/10; FCM là cấu hình mới, chưa đọc lại VPS. Có giá trị không đồng nghĩa key hợp lệ.

| Chức năng | Cần điền | Điều kiện ngoài key |
|---|---|---|
| Quét món ăn/chat ảnh | `Gemini__ApiKey` | `Gemini__Enabled=true`, model mà tài khoản được cấp quyền |
| Chat chữ/gợi ý bài tập | `Groq__ApiKey` đã có theo audit cũ | `Groq__Enabled=true`, model hỗ trợ JSON object; test phản hồi thật |
| Push Android | `FcmPush__ProjectId`, `FcmPush__ServiceAccountJsonBase64` | Cùng Firebase project với google-services.json của app; Cloud Messaging API hoạt động, service account có quyền gửi FCM |
| Lookup STK PT | `BankLookup__ClientId`, `BankLookup__ApiKey` | `BankLookup__Provider=VietQr`; quyền API lookup thật, không chỉ tạo mã QR |
| Meet PT | `GoogleMeet__ClientId`, `GoogleMeet__ClientSecret`, `GoogleMeet__RedirectUri` | Google Meet REST API + OAuth scope meetings.space.created, consent/test users; PT phải kết nối Google của họ |
| Thanh toán lịch PT | `Payos__ClientId`, `Payos__ApiKey`, `Payos__ChecksumKey`, `Payos__AccountNumber`, `Payos__ReturnUrl`, `Payos__CancelUrl` | Merchant được duyệt, webhook đăng ký, đối soát thanh toán/hoàn tiền thật |
| AI Plus Android | `GooglePlayBilling__ServiceAccountJsonBase64` | Play API permissions, subscription `fitmate_ai_plus`, prepaid base plan `monthly` trả trước 1 tháng (không tự gia hạn), package `vn.fitmate.app`, license testers |

Email OTP xóa tài khoản dùng SMTP/OTP email đã có, không cần key mới. GA4 app/web đã có property/stream/service account theo audit trước; không tạo property mới hay xóa lịch sử. SMS không bắt buộc khi lưu STK. R2 không bắt buộc khi dùng local storage bền vững. Python/model artifact/internal token chỉ cần khi chọn `python_service`; đường Gemini không cần sidecar Python.

Callback Meet cần đăng ký chính xác: `https://api.fitmate.site/api/v1/integrations/google-meet/callback` nếu đây là public API host đang dùng. Không dùng URL của landing page làm callback API.

## Cờ cấu hình sau khi có key

Đường hosted AI không cần tự dựng model server:

```dotenv
Ai__Enabled=true
Ai__Provider=gemini
Ai__FoodScanEnabled=true
Ai__CompanionProvider=groq
Ai__CompanionChatEnabled=true
Ai__WorkoutAdjustEnabled=true
AiUsage__Enabled=true
Gemini__Enabled=true
Groq__Enabled=true
FcmPush__Enabled=true
BankLookup__Provider=VietQr
```

Chỉ bật từng nhóm sau khi điền key tương ứng. Model chọn qua `Gemini__Model` và `Groq__Model`, phải còn được provider hỗ trợ và tài khoản có quyền; giữ tên thực tế của tài khoản thay vì coi giá trị mẫu là bằng chứng đã chạy.

FCM env là master switch; DI hiện không dùng cờ push cũ trong DB để override. FCM bật với JSON/key/project sai làm startup thất bại có chủ đích. Dùng service-account JSON mã hóa base64 nguyên file, không phải Web API key hay legacy FCM server key. Không dùng chung quyền quá rộng chỉ vì GA4 đã có service account.

PayOS: `Payment__Provider=payos`, `Payos__Enabled=true`, `Payos__MerchantApproved=true` chỉ sau merchant approval; ReturnUrl/CancelUrl HTTPS trỏ đúng API routes trong `.env.example`. Không giả MerchantApproved để vượt gate.

Play: giữ `GooglePlayBilling__SalesEnabled=false` cho tới khi test mua/restore/expiry/cancel và AI/quota thành công. Có thể bật verification `Enabled=true` khi đủ credentials/product. Mở bán cần cả scan/chat thật và `AiUsage__Enabled=true`; đã hỗ trợ cả Gemini hosted scan và Python scan.

Không bật permanent-deletion finalizer, automated PT payouts, VIP cũ hay cash rewards bằng cách điền tất cả cờ true. Đây là các gate nghiệp vụ/retention riêng, không phải mock bị bỏ sót. Không tự tạo tài nguyên trả phí.

## Lệnh kiểm chứng và cập nhật

Backend: unit suite đã chạy 1.048/1.048; integration project build 0 warning/error. PostgreSQL tests chưa chạy vì local 5432 không hoạt động. Chỉ dùng PostgreSQL TEST cô lập (không đặt DB_HOST/credentials production); harness tạo database tên `fitmate_integration_test_*`:

```powershell
Set-Location D:\Ky8\exe201\FitmateAI_BE
dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj
# Sau khi cấu hình PostgreSQL local/test theo runbook:
dotnet test tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj --filter "FullyQualifiedName~PushDeliveryConcurrencyTests|FullyQualifiedName~HostedFeatureEndpointTests|FullyQualifiedName~SocialProfileVisibilityTests"
```

Mobile/Admin chưa chạy install/typecheck/test/build theo quy tắc manual-execution của repo. Có dependency native FCM mới; cần cài và build native, Expo Go/hot reload không bổ sung module này:

```powershell
Set-Location D:\Ky8\exe201\FitmateAI_FE\mobile
npm ci
npm run type-check
npm test
npm run android
# Sau UAT mới build AAB, kiểm versionCode cao hơn bản đã tải Play:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-release-secure.ps1
Set-Location D:\Ky8\exe201\FitmateWebApp
npm run typecheck
npm run test:run
npm run build
```

Không chạy prebuild --clean để chữa lỗi vì có thể làm mất native changes. AAB: `mobile/android/app/build/outputs/bundle/release/app-release.aab`. Không upload Play khi chưa qua các bước trên.

Archive từng upload ngày 26/10 đã cũ. Sau commit, operator cần chạy lại script prepare để đóng gói source/test mới; script deploy giữ backup/migration preview và không thay `.env`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\Ky8\exe201\FitmateAI_BE\scripts\prepare-mobile-update.ps1
ssh -t -i "$env:USERPROFILE\.ssh\fitmate_vps_ed25519" fitmate@103.116.39.29 "sudo bash /home/fitmate/releases/mobile-update-20261026/deploy-mobile-update.sh"
```

Đây là lệnh operator chạy khi quyết định rollout, chưa được chạy trong lượt này. Rollback về code cũ không hiểu Sending=8: giữ push tắt khi rollback và xử lý/retry lease bằng bản mới, không tự reset ledger/user data.

## Bạn kiểm thử thật sau khi thêm key

1. Member/PT: đăng nhập, đổi mật khẩu, OTP xóa tài khoản sai/hết hạn/đúng, đúng thì logout; không gửi email hay xóa account người dùng thật để thử.
2. Push: cấp/từ chối quyền, foreground/background/app đóng, refresh token, logout/login user khác trên cùng máy, tắt quyền rồi trở lại. Tin user cũ không được lộ cho user mới; bấm tin mở inbox của user đang đăng nhập. Thử mạng lỗi/retry và hai worker đồng thời trên DB test.
3. AI: chat chữ/ảnh, ảnh món ăn/ảnh không có món ăn, nhập bài tập trống/quá dài, provider 401/429/timeout, quota hết. Lỗi không tính lượt thành công, không tạo meal/workout tự động; nhập tay vẫn chạy khi provider tắt.
4. PT: nhập STK/lookup sai và đúng, lưu không cần SMS; kết nối/ngắt Meet, booking online tạo Meet thật; Member đặt/thanh toán/xác nhận và review đúng PT; nhiều trang sessions/revenue và refund adjustment.
5. Cộng đồng: bài Public/Friends/Private, block hai chiều, đổi hồ sơ khi đang tải, phân trang, upload ảnh/bình luận và bàn phím; người khác không thấy số buổi tập riêng tư.
6. Admin: users/detail mới, certificate approve/reject, support/moderation, bookings/finance/manual settlement/audit server paging; account không có quyền bị từ chối; GA4 App/Landing tách riêng và không mất lịch sử.
7. Thanh toán/Play dùng test users/môi trường provider phù hợp; verify webhook/idempotency, duplicate callback không ghi hai lần, restore và revoke entitlement. Key presence và HTTP health không thay thế các bài này.

Nguồn kỹ thuật: [FCM HTTP v1](https://firebase.google.com/docs/cloud-messaging/send/v1-api), [React Native Firebase messaging](https://rnfirebase.io/messaging/usage), [Gemini image input](https://ai.google.dev/gemini-api/docs/image-understanding), [Groq JSON output](https://console.groq.com/docs/structured-outputs).
