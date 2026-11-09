# AI Plus: mua một lần cho một tháng

Ngày 2026-11-10. User yêu cầu thay checkout trả trước bằng sản phẩm tiêu hao; không chuyển quyền của giao dịch cũ sang người khác. Code đã soạn; chưa phải chứng nhận mua thật hay deploy.

## Cấu hình Play Console do chủ tài khoản thực hiện

1. Fitmate → Kiếm tiền qua Play → Sản phẩm → Sản phẩm tính phí một lần → Tạo sản phẩm.
2. Mã `fitmate_ai_plus_month`; tên `Fitmate AI Plus - 1 tháng`.
3. Mô tả: `100 lượt chat và gợi ý tập luyện mỗi ngày trong 1 tháng. Không tự gia hạn.` Không hứa quét món ăn.
4. Tạo tùy chọn **Mua** (buy), không thuê/rental, không đặt trước; không bật nhiều số lượng. Có thể dùng ID tùy chọn `buy-month`.
5. Phạm vi Việt Nam; giá niêm yết 29.000 VND; kiểm tra giá cuối cùng/thuế trong hộp Play, không tự tính số tiền người mua thực trả.
6. Lưu/kích hoạt sản phẩm và tùy chọn mua. Không xóa subscription `fitmate_ai_plus`/`monthly`: giao dịch đã trả tiền vẫn phải được xác minh và phục vụ.

## Backend và quyền sử dụng

- Giữ `GooglePlayBilling__Enabled=true`, thông tin service account/package/subscription cũ để tiếp tục xác minh người đã mua.
- Thêm `GooglePlayBilling__OneTimeProductId=fitmate_ai_plus_month`, `GooglePlayBilling__OneTimeMinimumAndroidVersionCode=8`.
- `GooglePlayBilling__OneTimeSalesEnabled=false` cho tới khi sản phẩm đã hoạt động và chuẩn bị kiểm thử nội bộ. Bật thành `true` cùng `SalesEnabled=true` để kiểm thử rồi nghiệm thu trước khi quảng bá bản mới công khai. Không nhúng key vào app.
- Không migration DB: ledger `ai_subscriptions` dùng `BasePlanId` rỗng cho sản phẩm một lần; hàng cũ giữ nguyên. Token mã hóa bởi key ring hiện hành; phải backup DB **và** key ring.
- Google `productsv2` xác minh sản phẩm, đúng một đơn vị, trạng thái PURCHASED, thời điểm thanh toán, mã đơn và obfuscated app-account ID. Thiếu/sai binding không tự cấp cho người ấn Khôi phục.
- Khóa giao dịch theo user + token. Một token cấp đúng một tháng lịch, bắt đầu từ ngày cấp quyền hoặc cuối hạn còn hiệu lực của chính user. Replay không kéo dài hạn. Mua đồng thời được tuần tự hóa. Không dùng email Google/người thanh toán làm khóa quyền.
- Ghi quyền và outbox `ai.purchase.complete` trong cùng transaction. Worker xác nhận/consume sau khi ghi bền vững, retry khi mạng lỗi; payload chỉ chứa UUID ledger. Consume không xóa quyền. Khi đăng nhập chỉ đọc ledger của user, không dựa vào danh sách CH Play còn giữ trên máy.
- Đơn thử nghiệm vẫn có thể nghiệm thu quyền trên tài khoản tester, nhưng `IsTestPurchase=true` loại khỏi báo cáo tiền thật. Báo cáo đơn mới/hoàn tiền dùng Orders API và outbox SignalR sẵn có; không ước tính net.
- API `/me/ai-subscription/verify` giữ cho thuê bao cũ; `/verify-purchase` dùng cho sản phẩm một lần. Header build dưới8 chỉ tắt **mua mới**, không tắt quyền/khôi phục. Header không cấp quyền mua.
- Reconcile đơn mới bằng worker server cho ledger đã biết; chưa có RTDN, nên không hứa tự khám phá mọi đơn chưa đến server. CLI `reconcile-ai-orders` hiện phục hồi **thuê bao cũ**, không dùng nó để gán sản phẩm mới chưa có binding.

## Kiểm thử bắt buộc trước bản công khai

Chạy unit và PostgreSQL integration mới `AiOneTimePurchaseIntegrationTests` trong DB test cô lập (không dùng DB/volume production). Docker local hiện chưa chạy; không ghi nhận các ca PostgreSQL là đã pass.

1. A mua → Plus100 và ngày hết hạn; logout/login A không bấm Khôi phục vẫn Plus.
2. Cùng Google thanh toán, đổi Fitmate B → mua đơn khác → B Plus; quay lại A vẫn giữ đúng ngày của A. Chờ worker hoàn tất consume trước lần mua kế tiếp nếu Play còn báo sở hữu giao dịch trước.
3. Gỡ/thêm Google trên thiết bị, cài lại app hoặc dùng thiết bị khác → login đọc quyền A từ server. Không xóa dữ liệu DB/key ring.
4. Khôi phục lặp, callback trùng và mất mạng sau thanh toán không nhân tháng; không cấp cho tài khoản khác. Pending/cancel không cấp quyền.
5. Mua thành công rồi đóng app trước callback → vào lại để server nhận giao dịch, cấp quyền và consume. Outbox thất bại phải retry, không mất quyền đã lưu.
6. Hoàn tiền/thu hồi thử nghiệm → reconcile cập nhật; admin không tính đơn test. Tiền Google chưa có giữ null, không làm mất các đơn đã có số liệu.
7. Người còn thuê bao cũ giữ hạn hiện hành; bản cũ vẫn đọc/khôi phục đúng, không mở checkout thuê bao mới.

Android chuẩn bị0.1.7/code8; không tự ký/build/upload/mua thật. Dùng license tester ở kênh nội bộ trước. Giá trong app lấy từ Play, quyền/expiry/quota lấy từ backend.

Nguồn Google: https://developer.android.com/google/play/billing/lifecycle/one-time ; https://developers.google.com/android-publisher/api-ref/rest/v3/purchases.productsv2 ; https://developers.google.com/android-publisher/api-ref/rest/v3/purchases.products/consume .
