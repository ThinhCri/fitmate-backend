# payOS và chi PT sau xác minh thủ công

Cập nhật 2026-09-26 theo yêu cầu chủ dự án. Code đã chuẩn bị, **chưa build/test/migrate hoặc nghiệm thu giao dịch thật**.

## Quyết định và phạm vi
Chọn payOS cho checkout theo từng booking: tạo link, query, webhook xác thực. payOS công bố luồng xác thực cá nhân bằng CCCD và tài khoản ngân hàng cùng tên. SePay cũng hỗ trợ tài khoản cá nhân; lựa chọn này dựa vào API đơn hàng phù hợp Fitmate, không phải khẳng định SePay không dùng được.

- [payOS: xác thực cá nhân](https://payos.vn/docs/huong-dan-su-dung/xac-thuc-to-chuc/)
- [payOS: API thanh toán](https://payos.vn/docs/api/)
- [payOS: chữ ký payment-requests](https://payos.vn/docs/tich-hop-webhook/kiem-tra-du-lieu-voi-signature/)
- [SePay: cổng thanh toán VietQR](https://sepay.vn/cong-thanh-toan-vietqr.html)

Việc hỗ trợ tài khoản cá nhân **không xác nhận** mô hình thu tiền khách cho nhiều PT, giữ tiền và phân chia 90/10 đã được duyệt. OPS-03 phải có xác nhận của provider cho mô hình thực tế và hoàn tất điều kiện vận hành trước bật live. Đây không phải dịch vụ escrow được nhà cung cấp cam kết. API payOS hiện ghi môi trường production; không giả rằng có sandbox miễn phí.

VNPay được giữ để đọc/xử lý dữ liệu lịch sử, không chọn đồng thời hai cổng cho một booking. Không tích hợp API chi hộ, không có worker tự trả tiền PT. Không thêm package SDK; adapter dùng HttpClient + HMAC theo tài liệu chính thức.

## Luồng chuẩn
1. Member giữ lịch, gửi `POST /api/v1/payments` với `{"hold_id":"<UUID>","method":"payos"}` và `Idempotency-Key`.
2. BE lưu Payment + payOS orderCode do DB cấp **trước** HTTP; retry dùng lại mã đó. FE mở `redirect_url` bằng trình duyệt, rồi đọc `GET /api/v1/me/payments/{id}`. Return/cancel URL không chứng minh đã trả tiền.
3. payOS POST `/api/v1/payments/payos/webhook`; BE kiểm HMAC dữ liệu gốc, tài khoản nhận, VND, số tiền, order/link/provider. Duplicate không tạo thêm thu/hoàn/chi.
4. Tiền đến khi hold còn hiệu lực: gắn payment vào lịch. Tiền đến muộn: lưu đã nhận + một Refund.Pending, không giành lại slot. Sai/thiếu/thừa tiền cần đối soát; không auto đánh dấu paid.
5. PT hoàn thành buổi; **Completed không tự duyệt chi**. Admin gọi khách và PT sau giờ kết thúc, xem hồ sơ buổi tập, xác minh người nhận tiền và kiểm tra tranh chấp.
6. Admin POST `/admin/finance/manual-settlements/{appointmentId}/approve` với `customerConfirmedAt`, `trainerConfirmedAt`, `sessionEvidenceReference`, `payeeEvidenceReference`, `noOpenDisputeConfirmed=true`. Timestamp UTC `Z`. BE tự tính số còn phải trả sau hoàn tiền, không nhận số tiền PT tùy ý từ client.
7. Nếu còn tranh chấp/thiếu chứng cứ: POST `.../{appointmentId}/hold` với `reason`. Phải duyệt lại sau xử lý. Refund pending luôn chặn chi dù trước đó approved.
8. Admin thực hiện chuyển khoản bằng ứng dụng ngân hàng của mình, sau đó POST `.../{appointmentId}/record-transfer` với `bankReference`, `evidenceReference`, `transferredAt`. API này **chỉ ghi nhận**, không gửi tiền. Một lần chuyển cho một buổi; chưa hỗ trợ gộp nhiều buổi trong một mã chuyển khoản.
9. Đối chiếu `GET /admin/finance/manual-settlements`, PT revenue/history và admin ledger. Không chuyển lần hai chỉ vì API timeout: kiểm tra sao kê và retry cùng mã chứng từ.

Tất cả đường dẫn bước 6–9 có prefix `/api/v1`. Mutation yêu cầu Admin, Idempotency-Key và X-Finance-Totp (6 chữ số hiện tại từ ứng dụng authenticator). Admin phải bật MFA; kiểm tra step-up được Application gọi trước mutation, không chỉ controller. Role được kiểm lại trong Application. Hồ sơ mới chưa review chưa có row; chọn booking Completed từ danh sách booking rồi approve/hold. `pending_review` là trạng thái hiển thị của revenue khi chưa có row.

“Record buổi PT” là hồ sơ/biên bản/chứng cứ buổi tập. Không mặc định bắt buộc ghi âm hoặc quay video. Reference phải trỏ tới hồ sơ private được operator quản lý; API hiện lưu reference, chưa tự xác thực quyền sở hữu file trong storage. Không đưa raw sức khỏe, nội dung cuộc gọi hay ảnh ngân hàng vào log/outbox.

## Hoàn tiền
- Route cũ `/admin/finance/refunds` từ chối payOS để tránh đánh dấu hoàn tiền khi chưa chuyển.
- `POST /admin/finance/manual-refunds`: `{paymentId,amountVnd,reason}` tạo intent Pending, giữ cap lũy kế và chặn chi PT.
- `GET /admin/finance/manual-refunds?page=1&pageSize=25`: lấy ID và trạng thái để xử lý.
- Sau chuyển tiền thật cho đúng khách, POST `.../manual-refunds/{refundId}/record-transfer` với `{bankReference,evidenceReference,transferredAt}`; lúc này mới Succeeded và điều chỉnh số tiền.
- Hủy link thanh toán không hoàn tiền. Không có auto-refund/payout. Refund sau payout không sửa chứng từ chi cũ; revenue thể hiện chênh lệch, operator phải đối soát khoản thu hồi/điều chỉnh trước đợt chi tiếp theo.

## Cấu hình khi đã được phép
Trong `.env`/secret manager, dùng tên ASP.NET Core đúng như `.env.example`: `Payment__Provider=payos`, `Payos__Enabled`, `Payos__MerchantApproved`, `Payos__ClientId`, `Payos__ApiKey`, `Payos__ChecksumKey`, `Payos__AccountNumber`, `Payos__ReturnUrl`, `Payos__CancelUrl`. Hai URL phải HTTPS thuộc frontend được kiểm soát. Webhook URL public HTTPS trỏ về API, cấu hình trong dashboard payOS bằng tay; không tự đăng ký webhook từ startup.

Mặc định Enabled/MerchantApproved/FEATURE_PAYMENT_ENABLED đều false. Không đặt true cho tới khi có approval, credentials, webhook và evidence. `Payment__AllowMockProvider=false`; chỉ cho true trong Development/Testing explicit. FE đọc `paymentsEnabled`, không hardcode bật thanh toán. Các credential không được đưa vào Expo/Vite env public.

## Lệnh user chạy tay
Agent không chạy các lệnh này. Dùng .NET 10 SDK và DB **cô lập**, backup trước mọi migration vào DB có dữ liệu.

```powershell
dotnet build Fitmate.slnx
# Build trên đã restore dependencies nếu cần; không build lại khi chạy tests:
dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --no-build --no-restore
dotnet ef migrations has-pending-model-changes --project src/Fitmate.Infrastructure --startup-project src/Fitmate.Api
dotnet ef migrations script 20260924115742_SupportAndPreferences 20260926120000_PayosAndManualSettlements --project src/Fitmate.Infrastructure --startup-project src/Fitmate.Api --output payos-migration-review.sql
# Chỉ sau khi kiểm tra DB_HOST/DB_PORT/DB_NAME là DB kiểm thử riêng:
dotnet ef database update --project src/Fitmate.Infrastructure --startup-project src/Fitmate.Api
# Fixture tự cấp DB test mới cho mỗi process nếu không đặt TEST_DB_NAME:
Remove-Item Env:TEST_DB_NAME -ErrorAction SilentlyContinue
dotnet test tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj --no-build --no-restore --filter "FullyQualifiedName~PayosPostgresIntegrationTests"
```

Migration/snapshot được soạn thủ công vì không chạy dotnet-ef; phải kiểm `has-pending-model-changes` trước áp dụng. Fixture integration dùng EnsureCreated trên DB mới mỗi process, không kiểm chứng migration chain. Nếu đặt TEST_DB_NAME, DB phải trống và tên bắt đầu bằng `fitmate_integration_test` hoặc `fitmate_payos_it_`; fixture từ chối DB đã có bảng thay vì chạy với schema cũ. User DB role cần quyền CREATEDB khi DB chưa tồn tại. Tên DB được in trong output; DB giữ lại sau test để chẩn đoán, cần quản lý dung lượng riêng. Không xóa volume DB để chữa lỗi. Kiểm chứng migration thực hiện riêng trên DB cô lập khác. Rollback Down chỉ cho bảng mới rỗng; khi có giao dịch dùng forward fix, không xóa chứng từ.

## Gate nghiệm thu còn mở
- Compile + toàn bộ tests; contract round-trip snake_case Payment, camelCase manual finance; test thật HTTP 401/403/400/404/409.
- DB migration up/down trên DB trống và upgrade bản sao; concurrency webhook, tạo payment, refund và transfer; timeout tạo link rồi retry phải một order.
- Xác nhận live nhỏ do operator thực hiện sau OPS-03; đối chiếu query, webhook, sao kê. Không tự gửi tiền để test.
- BE-803: verify thật MFA step-up mới bằng X-Finance-Totp: thiếu/sai code, tài khoản locked và chưa bật MFA đều bị từ chối; không log header này.
- BE-816/810: liên kết evidence với private storage/case có kiểm quyền; xử lý debit sau payout và ledger accounting đầy đủ; hiện mới có receipt per appointment và projection, chưa phải sổ kế toán bất biến hoàn chỉnh.
- Hai FE phải tích hợp/kiểm UAT theo contract mới; tài liệu đồng bộ không đồng nghĩa màn UI đã được làm.

Không đánh VERIFIED/production-ready chỉ từ code review.

## Đối soát và phục hồi
- Admin có thể GET `/api/v1/admin/finance/payments/{paymentId}/provider-status`; query dùng credentials BE, không thay đổi Paid từ dữ liệu client.
- Dùng kênh payOS/credentials riêng cho mỗi môi trường. Không để hai database độc lập cấp orderCode trên cùng kênh.
- Sau khôi phục backup, trước mở checkout mới phải đối soát các giao dịch phát sinh sau thời điểm backup và tăng high-water mark của identity `payos_orders.OrderCode` vượt mọi mã đã phát hành trên kênh (kể cả mã không có trong backup). Không tái sử dụng mã chỉ vì DB bị restore; ghi bằng chứng trong DR runbook.
- File `.env` dành Compose không tự được `dotnet` CLI đọc. Trước lệnh EF/test cần có DB_HOST/DB_PORT/DB_USER/DB_PASSWORD và JWT_SIGNING_KEY trong môi trường terminal theo local runbook. Không gửi các giá trị secrets trong log hỗ trợ.
- Integration test fixture dùng `TEST_DB_NAME`, còn EF CLI dùng `DB_NAME`: hai lệnh phải trỏ đúng DB kiểm chứng tương ứng. Không chạy database update vào DB đang dùng trước khi đã xem SQL và backup.
