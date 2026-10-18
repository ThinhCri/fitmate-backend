# Gói AI Plus cho tài khoản cá nhân — thiết kế triển khai

Trạng thái: đã viết luồng Google Play Billing trong code; **chưa mở bán/chưa nghiệm thu giao dịch thật**. Chủ sản phẩm đã chọn **199.000đ cho gói trả trước 1 tháng, không tự gia hạn** ngày 2026-10-18. Giá thật vẫn phải cấu hình và xác nhận trong Play Console. Đây là sản phẩm số, tách khỏi booking/gói PT và payOS. Không dùng `vip_purchase`/điểm thưởng cũ làm bằng chứng đã trả tiền.

## Luồng người dùng

1. Trang Tài khoản và các màn AI hiển thị hạn mức chat/quét còn lại, thời điểm làm mới theo `Asia/Ho_Chi_Minh`, và so sánh Miễn phí/AI Plus. Màn quét vẫn cho ghi bữa ăn thủ công khi hết lượt; lịch sử chat/nhật ký không bị khóa.
2. Người dùng chọn AI Plus trả trước một tháng; app lấy giá địa phương và ưu đãi (nếu có) trực tiếp từ Google Play. Không tự tính hoặc ghi cứng giá. Trước mua phải nói rõ thanh toán một lần, không tự gia hạn và quyền lợi cụ thể. Một tháng Play theo lịch, không hứa cố định 30 × 24 giờ.
3. Google Play Billing xử lý mua. App gửi purchase token và product ID đến BE qua phiên đăng nhập. BE chỉ cấp quyền sau khi Google Play Developer API xác nhận token thuộc package/product đã cấu hình, trạng thái còn hiệu lực và token chưa gắn tài khoản khác. Callback/deep link hoặc ảnh chụp giao dịch không đủ bằng chứng.
4. BE ghi nhận entitlement theo tài khoản. App làm mới trạng thái từ BE; khôi phục mua hàng dùng lại token hợp lệ của cùng tài khoản. Đổi thiết bị/đăng nhập không mất quyền. Không cho chuyển subscription giữa hai tài khoản Fitmate bằng cách gửi lại token.
5. Gói tự hết hạn theo `expiryTime` được Google xác minh, không có luồng hủy gia hạn hoặc grace period của gói tự gia hạn. Hết hạn, hoàn tiền hoặc thu hồi: ngừng Plus; dữ liệu cũ không bị xóa. Backend đối chiếu lại Developer API khi đọc/tiêu thụ AI và qua worker định kỳ. RTDN/Pub/Sub chưa được cấu hình trong bản này; nếu cần thời gian phản ánh gần tức thì, đó là gate riêng trước khi cam kết SLA. Token không còn tồn tại trên Google được đánh dấu thu hồi.

## Quy tắc hạn mức đề xuất ban đầu

| Quyền | Miễn phí | AI Plus tháng |
|---|---:|---:|
| Trả lời chatbot | 10 lần/ngày | 100 lần/ngày |
| Quét ảnh món ăn | 2 lần/ngày | 20 lần/ngày |

Đây là **mức khởi đầu cấu hình được**, phải nghiệm thu chi phí model trước khi công bố. Mỗi yêu cầu thành công tính 1 lượt, kể cả khi đính kèm nhiều ảnh (tối đa giới hạn upload hiện tại); lỗi provider, timeout, hủy trước kết quả không trừ lượt. Không bán “AI không giới hạn”, không cộng dồn lượt ngày. Ngày tính theo Việt Nam, làm mới lúc 00:00 địa phương. Nếu tăng/giảm hạn mức, phải công bố minh bạch; không âm thầm giảm quyền trong kỳ trả tiền.

Reservation quota được khóa atomic trên PostgreSQL cho từng account/loại/ngày để hai thiết bị không vượt ngưỡng. Reservation hết hạn sau 10 phút nếu tiến trình chết; lỗi AI được release. API chat/quét chưa có idempotency ở mức kết quả toàn yêu cầu, vì vậy người dùng chủ động gửi lại một yêu cầu đã thành công sau mất kết nối vẫn có thể tốn thêm một lượt. Cần kiểm tra trường hợp này trong UAT trước khi công bố chính sách hạn mức. Mã lỗi hết lượt riêng với lỗi model 429/503.

## Ranh giới kỹ thuật và vận hành

- Một subscription product `fitmate_ai_plus` với base plan `monthly` **loại prepaid** ở bản đầu. Backend từ chối giao dịch có `autoRenewingPlan` hoặc thiếu `prepaidPlan`, kể cả khi product/base plan ID đúng. Không bán năm.
- Product ID, package name và service-account credentials của Google Play chỉ ở server/secret manager; không đưa vào `EXPO_PUBLIC_*`. Giá do Play Console quản lý; BE không tự tạo một payment payOS cho sản phẩm số.
- Android đã thêm `expo-iap` và config plugin; Expo Go không hỗ trợ mua thử. Dùng internal testing + license testers và Play Console subscription thật trước khi mở nút checkout. Cần cấu hình Google Play Developer API và service account quyền phù hợp. Không đặt credential trong app.
- Admin chỉ xem trạng thái, token hash/ID rút gọn, lịch sử đối soát và điều tra tranh chấp; không có nút tự cấp Plus thay cho thanh toán. Không log purchase token thô, khóa Google hoặc nội dung chat/ảnh.
- Không bật feature flag bán gói cho tới khi unit/integration/UAT xác nhận: mua mới, từ chối gói auto-renew, hết hạn, refund/revoke, token replay khác tài khoản, lỗi Google API, concurrent quota, khôi phục mua hàng và account deletion. Cần dừng bán đủ sớm để phục vụ hết kỳ đã thanh toán khi dự kiến đóng ứng dụng.

## Cấu hình và nghiệm thu cần thực hiện sau khi có Play Console

1. Trong Play Console tạo app Android đúng package `vn.fitmate.app`, subscription product `fitmate_ai_plus`, base plan `monthly` **prepaid, 1 tháng, không auto-renew**, giá Việt Nam mục tiêu 199.000đ; xác nhận giá cuối cùng, mô tả/quyền lợi khớp hạn mức cấu hình. Tạo license tester và phát hành internal testing AAB đã ký. **Không thể kiểm thử thanh toán thật chỉ bằng Expo Go hoặc APK cài trực tiếp ngoài Play**.
2. Liên kết Play Console với Google Cloud project, bật Google Play Android Developer API; tạo service account có quyền xem subscription và quản lý đơn hàng cần thiết cho `subscriptionsv2.get`/acknowledge. Xuất JSON và mã hóa ngay tại máy vận hành bằng PowerShell: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\duong-dan\play-service-account.json"))`. Chỉ đặt kết quả vào `GooglePlayBilling__ServiceAccountJsonBase64` trong `.env` backend hoặc secret manager; không dán JSON/key vào mobile, tài liệu, chat hoặc Git.
3. Backend: cấu hình `GooglePlayBilling__Enabled=true` để xác minh giao dịch, package/product/base plan giữ đúng các ID trên. `GooglePlayBilling__SalesEnabled` mặc định `false`; chỉ bật `true` sau khi AI thật, giá, quyền lợi, hạn mức và UAT đạt. Startup từ chối mở bán khi hai capability/quota hoặc provider scan/chat chưa cấu hình. Khi ngừng bán, tắt riêng `SalesEnabled`, giữ `Enabled=true` để đối chiếu/khôi phục giao dịch của khách đang còn kỳ; API trả `billing_available=false`, `restore_available=true`. `AiUsage__Enabled=true` chỉ khi hạn mức và đường AI thật hoạt động. Giữ volume DataProtection key ring bền vững, nếu mất key ring thì token đã mã hóa không đối chiếu lại được.
4. Chạy migration `20261011040000_AddAiUsageReservations` và `20261011050000_AddGooglePlayAiSubscriptions` trên DB local có backup; build/restart Backend. Android cần **build native mới** sau khi thêm `expo-iap`, rồi cài từ track internal testing bằng tài khoản Play tester.
5. UAT: mua prepaid → BE xác minh/ack → Plus; khôi phục đúng tài khoản, token khác tài khoản 409; gói auto-renew bị từ chối; pending/expired/refund không có Plus; hết kỳ không phát sinh giao dịch tự động; hạn mức hai thiết bị không vượt; lỗi Google/API không cấp quyền giả. Kiểm tra thanh toán và trạng thái trực tiếp trên Play Console trước khi bật bán cho người khác.

Bundle AI riêng đã được sửa theo sự cho phép của chủ sản phẩm nhưng chưa triển khai và thử model thật. Giao dịch Play, ký AAB và quyền service account **chưa được nghiệm thu**. Mạng/timeout của Google được trả về lỗi dịch vụ 503 và không cấp Plus; thao tác bị chính người dùng hủy vẫn tôn trọng cancellation. Không dùng PayOS/VIP cũ để bán AI Plus.

Nguồn chính thức: [Google Play Payments policy](https://support.google.com/googleplay/android-developer/answer/9858738), [Play Billing backend](https://developer.android.com/google/play/billing/backend), [subscriptions lifecycle](https://developer.android.com/google/play/billing/lifecycle/subscriptions), [Expo IAP](https://docs.expo.dev/guides/in-app-purchases/).
